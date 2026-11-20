using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Tests;

[Collection(AppCollection.Name)]
public sealed class AcademicYearTests(AppFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<HttpResponseMessage> SendInYearAsync(HttpClient client, HttpMethod method, string url, Guid yearId, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(AcademicYearScope.HeaderName, yearId.ToString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<JsonNode> GetInYearAsync(HttpClient client, string url, Guid yearId)
    {
        var response = await SendInYearAsync(client, HttpMethod.Get, url, yearId);
        Assert.True(response.IsSuccessStatusCode, $"GET {url} returned {(int)response.StatusCode}");
        return await response.ReadJsonAsync();
    }

    private static async Task<JsonNode> YearsAsync(TestAcademy academy) => await academy.Admin.GetJsonAsync("/api/academic-years");

    private static Guid CurrentYearId(JsonNode years) => years["currentId"]!.GetValue<Guid>();

    /// <summary>Adds the twelve months after the current year, which the API allows because it has not ended.</summary>
    private static async Task<Guid> AddNextYearAsync(TestAcademy academy)
    {
        var current = (await YearsAsync(academy))["items"]!.AsArray().Single(y => y!["isCurrent"]!.GetValue<bool>())!;
        var start = DateOnly.Parse(current["endDate"]!.GetValue<string>()).AddDays(1);
        var response = await academy.Admin.PostAsJsonAsync("/api/academic-years",
            new { name = $"Next {start.Year}", startDate = start, endDate = start.AddYears(1).AddDays(-1) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync())["id"]!.GetValue<Guid>();
    }

    /// <summary>Past years cannot be created through the API, so tests that need one insert it directly.</summary>
    private Task<Guid> InsertEndedYearAsync(TestAcademy academy) => factory.WithDbAsync(async db =>
    {
        var current = await db.AcademicYears.IgnoreQueryFilters().Where(y => y.TenantId == academy.Id).OrderBy(y => y.StartDate).FirstAsync();
        var year = new AcademicYear
        {
            TenantId = academy.Id,
            Name = "Last year",
            StartDate = current.StartDate.AddYears(-1),
            EndDate = current.StartDate.AddDays(-1)
        };
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();
        return year.Id;
    });

    [Fact]
    public async Task New_academy_starts_with_a_current_april_to_march_year()
    {
        var academy = await factory.NewAcademyAsync();

        var years = await YearsAsync(academy);
        var year = years["items"]!.AsArray().Single()!;

        Assert.True(year["isCurrent"]!.GetValue<bool>());
        Assert.False(year["isClosed"]!.GetValue<bool>());
        Assert.True(years["canManage"]!.GetValue<bool>());
        var start = DateOnly.Parse(year["startDate"]!.GetValue<string>());
        Assert.Equal((4, 1), (start.Month, start.Day));
        Assert.Equal(start.AddYears(1).AddDays(-1).ToString("yyyy-MM-dd"), year["endDate"]!.GetValue<string>());
    }

    [Fact]
    public async Task Students_are_listed_per_year_and_admitted_into_the_selected_year()
    {
        var academy = await factory.NewAcademyAsync();
        var courseId = await academy.NewCourseAsync();
        await academy.NewStudentAsync(courseId, name: "This Year Student");
        var nextYear = await AddNextYearAsync(academy);

        var admitted = await SendInYearAsync(academy.Admin, HttpMethod.Post, "/api/students", nextYear,
            new { fullName = "Next Year Student", courseId, feeAmount = 1000 });
        Assert.Equal(HttpStatusCode.Created, admitted.StatusCode);

        var current = await academy.Admin.GetJsonAsync("/api/students");
        var next = await GetInYearAsync(academy.Admin, "/api/students", nextYear);
        Assert.Equal("This Year Student", current["items"]![0]!["fullName"]!.GetValue<string>());
        Assert.Equal(1, current["total"]!.GetValue<int>());
        Assert.Equal("Next Year Student", next["items"]![0]!["fullName"]!.GetValue<string>());
        Assert.Equal(1, next["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task Ended_year_is_locked_and_closed_for_admissions()
    {
        var academy = await factory.NewAcademyAsync();
        var courseId = await academy.NewCourseAsync();
        var ended = await InsertEndedYearAsync(academy);

        var edit = await academy.Admin.PutAsJsonAsync($"/api/academic-years/{ended}",
            new { name = "Renamed", startDate = Today.AddYears(-2), endDate = Today.AddDays(10) });
        var admit = await SendInYearAsync(academy.Admin, HttpMethod.Post, "/api/students", ended,
            new { fullName = "Too Late", courseId, feeAmount = 1000 });

        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
        Assert.Contains("ended", await edit.ErrorAsync());
        Assert.Equal(HttpStatusCode.BadRequest, admit.StatusCode);
        Assert.True((await YearsAsync(academy))["items"]!.AsArray().Single(y => y!["id"]!.GetValue<Guid>() == ended)!["isClosed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Years_cannot_overlap_or_be_created_in_the_past()
    {
        var academy = await factory.NewAcademyAsync();
        var currentId = CurrentYearId(await YearsAsync(academy));

        var overlapping = await academy.Admin.PostAsJsonAsync("/api/academic-years",
            new { name = "Overlap", startDate = Today, endDate = Today.AddMonths(6) });
        var past = await academy.Admin.PostAsJsonAsync("/api/academic-years",
            new { name = "Old", startDate = Today.AddYears(-5), endDate = Today.AddYears(-4) });
        var reversed = await academy.Admin.PutAsJsonAsync($"/api/academic-years/{currentId}",
            new { name = "Bad", startDate = Today.AddMonths(1), endDate = Today });

        Assert.Equal(HttpStatusCode.Conflict, overlapping.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
    }

    [Fact]
    public async Task Super_admin_can_change_the_current_year_but_an_admin_cannot()
    {
        var academy = await factory.NewAcademyAsync();
        var admin = await academy.NewUserAsync("Admin");
        var currentId = CurrentYearId(await YearsAsync(academy));
        var newDates = new { name = "2026-27 (June)", startDate = Today.AddMonths(-5), endDate = Today.AddMonths(7) };

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/academic-years/{currentId}", newDates)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/academic-years", newDates)).StatusCode);
        Assert.False((await admin.GetJsonAsync("/api/academic-years"))["canManage"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PutAsJsonAsync($"/api/academic-years/{currentId}", newDates)).StatusCode);
    }

    [Fact]
    public async Task Money_and_tests_follow_their_dates_into_the_matching_year()
    {
        var academy = await factory.NewAcademyAsync();
        var subjectId = await academy.NewSubjectAsync("Physics");
        var courseId = await academy.NewCourseWithSubjectsAsync("Foundation", subjectId);
        var studentId = await academy.NewStudentAsync(courseId, fee: 10000);
        await academy.PayAsync(studentId, 4000);
        await academy.Admin.PostAsJsonAsync("/api/expenses", new { title = "Rent", amount = 500, category = "Rent", method = "Cash" });
        await academy.Admin.PostAsJsonAsync("/api/tests", new { title = "Unit 1", subjectId, courseId, maxMarks = 50 });
        var nextYear = await AddNextYearAsync(academy);

        Assert.Equal(1, (await academy.Admin.GetJsonAsync("/api/payments"))["total"]!.GetValue<int>());
        Assert.Equal(1, (await academy.Admin.GetJsonAsync("/api/expenses"))["total"]!.GetValue<int>());
        Assert.Equal(1, (await academy.Admin.GetJsonAsync("/api/tests"))["total"]!.GetValue<int>());
        Assert.Equal(0, (await GetInYearAsync(academy.Admin, "/api/payments", nextYear))["total"]!.GetValue<int>());
        Assert.Equal(0, (await GetInYearAsync(academy.Admin, "/api/expenses", nextYear))["total"]!.GetValue<int>());
        Assert.Equal(0, (await GetInYearAsync(academy.Admin, "/api/tests", nextYear))["total"]!.GetValue<int>());
        Assert.Equal(0, (await GetInYearAsync(academy.Admin, "/api/dashboard", nextYear))["finance"]!.Money("totalRevenue"));
        Assert.Equal(4000, (await academy.Admin.GetJsonAsync("/api/dashboard"))["finance"]!.Money("totalRevenue"));
    }

    [Fact]
    public async Task Dues_from_an_earlier_year_stay_visible_and_collectable()
    {
        var academy = await factory.NewAcademyAsync();
        var courseId = await academy.NewCourseAsync();
        var studentId = await academy.NewStudentAsync(courseId, fee: 9000, name: "Carried Over");
        var ended = await InsertEndedYearAsync(academy);
        await factory.WithDbAsync(async db =>
        {
            var student = await db.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == studentId);
            student.AcademicYearId = ended;
            return await db.SaveChangesAsync();
        });

        var finance = (await academy.Admin.GetJsonAsync("/api/dashboard"))["finance"]!;
        var lookup = (await academy.Admin.GetJsonAsync("/api/students/lookup?search=Carried")).AsArray();
        var payment = await academy.PayAsync(studentId, 2000);

        Assert.Equal(9000, finance.Money("earlierYearsPending"));
        Assert.Equal(0, finance.Money("pendingFees"));
        Assert.Equal("Last year", lookup.Single()!["academicYearName"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Created, payment.StatusCode);
    }

    [Fact]
    public async Task Another_academys_year_id_is_ignored()
    {
        var a = await factory.NewAcademyAsync();
        var b = await factory.NewAcademyAsync();
        await a.NewStudentAsync(await a.NewCourseAsync());
        var foreignYear = CurrentYearId(await YearsAsync(b));

        var students = await GetInYearAsync(a.Admin, "/api/students", foreignYear);

        Assert.Equal(1, students["total"]!.GetValue<int>());
    }

    [Fact]
    public void Current_year_is_the_one_containing_today_or_the_latest_started()
    {
        var today = new DateOnly(2026, 11, 17);
        var last = (Guid.NewGuid(), new DateOnly(2025, 4, 1), new DateOnly(2026, 3, 31));
        var gapYear = (Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 10, 31));
        var next = (Guid.NewGuid(), new DateOnly(2027, 4, 1), new DateOnly(2028, 3, 31));
        var containing = (Guid.NewGuid(), new DateOnly(2026, 11, 1), new DateOnly(2027, 3, 31));

        Assert.Equal(containing.Item1, AcademicYearScope.CurrentOf([last, gapYear, containing, next], today));
        Assert.Equal(gapYear.Item1, AcademicYearScope.CurrentOf([last, gapYear, next], today));
        Assert.Equal(next.Item1, AcademicYearScope.CurrentOf([next], today));
        Assert.Null(AcademicYearScope.CurrentOf([], today));
    }
}
