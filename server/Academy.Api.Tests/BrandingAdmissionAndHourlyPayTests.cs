using System.Net;
using System.Net.Http.Json;
using Academy.Api.Domain;
using Academy.Api.Features;
using Academy.Api.Tests.Support;

namespace Academy.Api.Tests;

[Collection(AppCollection.Name)]
public sealed class BrandingTests(AppFactory factory)
{
    /// <summary>The smallest valid PNG: a 1×1 transparent pixel.</summary>
    private const string TinyPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    [Fact]
    public async Task Super_admin_sets_logo_and_tagline_and_every_user_sees_them()
    {
        var academy = await factory.NewAcademyAsync();
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PutAsJsonAsync("/api/branding", new { tagline = "Excellence in coaching" })).StatusCode);
        var upload = await academy.Admin.PutAsJsonAsync("/api/branding/logo", new { dataUrl = TinyPng });
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var staff = await academy.NewUserAsync("Staff");
        var tenant = (await staff.GetJsonAsync("/api/auth/session"))["tenant"]!;
        Assert.Equal("Excellence in coaching", tenant["tagline"]!.GetValue<string>());
        var logoUrl = tenant["logoUrl"]!.GetValue<string>();

        // Served without signing in, so the sign-in page, admission form and browser tab can show it.
        var image = await factory.NewBrowser().GetAsync(logoUrl);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);

        var profile = await factory.NewBrowser().GetJsonAsync($"/api/public/academies/{academy.Code}");
        Assert.Equal(logoUrl, profile["logoUrl"]!.GetValue<string>());
    }

    [Fact]
    public async Task Only_the_super_admin_and_the_owner_can_change_branding()
    {
        var academy = await factory.NewAcademyAsync();
        var admin = await academy.NewUserAsync("Admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync("/api/branding", new { tagline = "Nope" })).StatusCode);

        var byOwner = await academy.Owner.PutAsJsonAsync($"/api/platform/tenants/{academy.Id}/branding", new { tagline = "Set by owner" });
        Assert.Equal(HttpStatusCode.OK, byOwner.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await academy.Owner.PutAsJsonAsync($"/api/platform/tenants/{academy.Id}/branding/logo", new { dataUrl = TinyPng })).StatusCode);
        Assert.Equal("Set by owner", (await academy.Admin.GetJsonAsync("/api/branding"))["tagline"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciLz4=")]
    [InlineData("data:image/png;base64,bm90IGFuIGltYWdl")]
    [InlineData("not a data url")]
    public void Only_real_png_jpeg_or_webp_images_are_accepted(string dataUrl) =>
        Assert.False(BrandingStore.TryDecode(dataUrl, out _, out _, out _));

    [Fact]
    public void Image_type_comes_from_the_bytes_not_the_label()
    {
        Assert.True(BrandingStore.TryDecode(TinyPng.Replace("image/png", "image/jpeg"), out _, out var contentType, out _));
        Assert.Equal("image/png", contentType);
    }
}

[Collection(AppCollection.Name)]
public sealed class AdmissionTests(AppFactory factory)
{
    private static object Application(Guid courseId, string phone = "9876543210", string website = "") => new
    {
        fullName = "Meera Iyer", phone, email = "meera@example.com", courseId, guardianName = "R. Iyer",
        address = "12 MG Road", message = "Evening batch please", website
    };

    [Fact]
    public async Task Application_waits_for_approval_then_becomes_a_student()
    {
        var academy = await factory.NewAcademyAsync();
        var courseId = await academy.NewCourseAsync(fee: 12000);
        var visitor = factory.NewBrowser();

        var form = await visitor.GetJsonAsync($"/api/public/admissions/{academy.Code}");
        Assert.True(form["open"]!.GetValue<bool>());
        Assert.Single(form["courses"]!.AsArray());

        Assert.Equal(HttpStatusCode.OK, (await visitor.PostAsJsonAsync($"/api/public/admissions/{academy.Code}", Application(courseId))).StatusCode);
        Assert.Equal(0, (await academy.Admin.GetJsonAsync("/api/students"))["total"]!.GetValue<int>());

        var queue = await academy.Admin.GetJsonAsync("/api/admissions");
        Assert.Equal(1, queue["counts"]!["Pending"]!.GetValue<int>());
        var applicationId = queue["page"]!["items"]![0]!["id"]!.GetValue<Guid>();

        var approve = await academy.Admin.PostAsJsonAsync($"/api/admissions/{applicationId}/approve", new { courseId, feeAmount = 11000 });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var student = await academy.StudentAsync((await approve.ReadJsonAsync())["studentId"]!.GetValue<Guid>());
        Assert.Equal("Meera Iyer", student["fullName"]!.GetValue<string>());
        Assert.Equal(11000, student.Money("feeAmount"));
        Assert.Contains("Guardian: R. Iyer", student["remarks"]!.GetValue<string>());

        var again = await academy.Admin.PostAsJsonAsync($"/api/admissions/{applicationId}/approve", new { courseId, feeAmount = 11000 });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Rejected_applications_never_reach_the_student_list()
    {
        var academy = await factory.NewAcademyAsync();
        var courseId = await academy.NewCourseAsync();
        await factory.NewBrowser().PostAsJsonAsync($"/api/public/admissions/{academy.Code}", Application(courseId));
        var id = (await academy.Admin.GetJsonAsync("/api/admissions"))["page"]!["items"]![0]!["id"]!.GetValue<Guid>();

        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PostAsJsonAsync($"/api/admissions/{id}/reject", new { reason = "Batch is full" })).StatusCode);
        Assert.Equal(0, (await academy.Admin.GetJsonAsync("/api/students"))["total"]!.GetValue<int>());
        Assert.Equal(1, (await academy.Admin.GetJsonAsync("/api/admissions?status=Rejected"))["page"]!["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task Closed_form_bots_and_duplicates_are_turned_away()
    {
        var academy = await factory.NewAcademyAsync();
        var courseId = await academy.NewCourseAsync();
        var visitor = factory.NewBrowser();
        var url = $"/api/public/admissions/{academy.Code}";

        // A bot filling the hidden field is told "ok" but nothing is stored.
        Assert.Equal(HttpStatusCode.OK, (await visitor.PostAsJsonAsync(url, Application(courseId, website: "http://spam"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.PostAsJsonAsync(url, Application(courseId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await visitor.PostAsJsonAsync(url, Application(courseId))).StatusCode);
        Assert.Equal(1, (await academy.Admin.GetJsonAsync("/api/admissions"))["counts"]!["Pending"]!.GetValue<int>());

        await academy.Admin.PutAsJsonAsync("/api/admissions/settings", new { open = false });
        Assert.False((await visitor.GetJsonAsync(url))["open"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.BadRequest, (await visitor.PostAsJsonAsync(url, Application(courseId, phone: "9000000000"))).StatusCode);
    }

    [Fact]
    public async Task An_academy_cannot_see_or_approve_another_academys_applications()
    {
        var first = await factory.NewAcademyAsync();
        var second = await factory.NewAcademyAsync();
        var courseId = await first.NewCourseAsync();
        await factory.NewBrowser().PostAsJsonAsync($"/api/public/admissions/{first.Code}", Application(courseId));
        var id = (await first.Admin.GetJsonAsync("/api/admissions"))["page"]!["items"]![0]!["id"]!.GetValue<Guid>();

        Assert.Equal(0, (await second.Admin.GetJsonAsync("/api/admissions"))["counts"]!["Pending"]!.GetValue<int>());
        var foreign = await second.Admin.PostAsJsonAsync($"/api/admissions/{id}/approve", new { courseId, feeAmount = 1 });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Course_from_another_academy_is_refused_on_the_public_form()
    {
        var first = await factory.NewAcademyAsync();
        var second = await factory.NewAcademyAsync();
        await first.NewCourseAsync();
        var foreignCourse = await second.NewCourseAsync();

        var response = await factory.NewBrowser().PostAsJsonAsync($"/api/public/admissions/{first.Code}", Application(foreignCourse));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

[Collection(AppCollection.Name)]
public sealed class HourlyPayTests(AppFactory factory)
{
    private static async Task<Guid> HourlyFacultyAsync(TestAcademy academy, decimal rate)
    {
        var response = await academy.Admin.PostAsJsonAsync("/api/faculty", new { fullName = "Neha Sharma", payType = "Hourly", hourlyRate = rate });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync())["id"]!.GetValue<Guid>();
    }

    [Theory]
    [InlineData(85, 600, 850)]
    [InlineData(90, 600, 900)]
    [InlineData(45, 500, 375)]
    [InlineData(1, 100, 1.67)]
    public void Session_pay_is_minutes_times_the_hourly_rate(int minutes, decimal rate, decimal expected) =>
        Assert.Equal(expected, FacultySession.AmountFor(minutes, rate));

    [Fact]
    public async Task Logged_minutes_decide_what_an_hourly_faculty_member_is_owed()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await HourlyFacultyAsync(academy, 600);

        Assert.Equal(HttpStatusCode.Created, (await academy.Admin.PostAsJsonAsync("/api/faculty-sessions", new { facultyId, minutes = 85, topic = "Algebra" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await academy.Admin.PostAsJsonAsync("/api/faculty-sessions", new { facultyId, minutes = 90 })).StatusCode);

        var faculty = (await academy.Admin.GetJsonAsync("/api/faculty")).AsArray().Single()!;
        Assert.Equal(175, faculty["minutesTaught"]!.GetValue<int>());
        Assert.Equal(1750, faculty.Money("earned"));
        Assert.Equal(1750, (await academy.Admin.GetJsonAsync("/api/dashboard"))["finance"]!.Money("facultyPayable"));

        Assert.Equal(HttpStatusCode.BadRequest, (await academy.Admin.PostAsJsonAsync("/api/payouts", new { facultyId, amount = 1750.01m, method = "UPI" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await academy.Admin.PostAsJsonAsync("/api/payouts", new { facultyId, amount = 1750, method = "UPI" })).StatusCode);
    }

    [Fact]
    public async Task A_rate_change_does_not_reprice_sessions_already_logged()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await HourlyFacultyAsync(academy, 600);
        await academy.Admin.PostAsJsonAsync("/api/faculty-sessions", new { facultyId, minutes = 60 });

        await academy.Admin.PutAsJsonAsync($"/api/faculty/{facultyId}", new { fullName = "Neha Sharma", payType = "Hourly", hourlyRate = 900 });
        await academy.Admin.PostAsJsonAsync("/api/faculty-sessions", new { facultyId, minutes = 60 });

        Assert.Equal(1500, (await academy.Admin.GetJsonAsync("/api/faculty")).AsArray().Single()!.Money("earned"));
    }

    [Fact]
    public async Task Paid_sessions_cannot_be_voided_and_bad_minutes_are_refused()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await HourlyFacultyAsync(academy, 600);
        foreach (var minutes in new[] { 0, -30, 1441 })
            Assert.Equal(HttpStatusCode.BadRequest, (await academy.Admin.PostAsJsonAsync("/api/faculty-sessions", new { facultyId, minutes })).StatusCode);

        var session = await (await academy.Admin.PostAsJsonAsync("/api/faculty-sessions", new { facultyId, minutes = 60 })).ReadJsonAsync();
        await academy.Admin.PostAsJsonAsync("/api/payouts", new { facultyId, amount = 600, method = "Cash" });

        var voiding = await academy.Admin.PostAsJsonAsync($"/api/faculty-sessions/{session["id"]}/void", new { reason = "Logged twice" });
        Assert.Equal(HttpStatusCode.BadRequest, voiding.StatusCode);
    }

    [Fact]
    public async Task Package_faculty_cannot_have_minutes_logged()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = (await (await academy.Admin.PostAsJsonAsync("/api/faculty", new { fullName = "Ravi", agreedPayout = 300000 })).ReadJsonAsync())["id"]!.GetValue<Guid>();

        var response = await academy.Admin.PostAsJsonAsync("/api/faculty-sessions", new { facultyId, minutes = 60 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var faculty = (await academy.Admin.GetJsonAsync("/api/faculty")).AsArray().Single()!;
        Assert.Equal("Package", faculty["payType"]!.GetValue<string>());
        Assert.Equal(300000, faculty.Money("earned"));
    }
}
