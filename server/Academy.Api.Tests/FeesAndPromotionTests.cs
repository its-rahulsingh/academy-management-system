using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Academy.Api.Domain;
using Academy.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Tests;

[Collection(AppCollection.Name)]
public sealed class FeesAndPromotionTests(AppFactory factory)
{
    private static async Task<Guid> StudentWithEmailAsync(TestAcademy academy, Guid courseId, decimal fee, string name, string email)
    {
        var response = await academy.Admin.PostAsJsonAsync("/api/students", new { fullName = name, email, courseId, feeAmount = fee });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync())["id"]!.GetValue<Guid>();
    }

    private Task<List<EmailMessage>> MailsAsync(string to) =>
        factory.WithDbAsync(db => db.EmailMessages.IgnoreQueryFilters().Where(m => m.ToAddress == to).OrderBy(m => m.CreatedAtUtc).ToListAsync());

    [Fact]
    public async Task Payments_refunds_and_cancellations_are_emailed_without_the_agreed_fee()
    {
        var academy = await factory.NewAcademyAsync();
        var email = $"parent-{Guid.NewGuid():N}@example.com";
        var student = await StudentWithEmailAsync(academy, await academy.NewCourseAsync("JEE Foundation", 60000), 60000, "Aman Verma", email);
        await academy.Admin.PostAsJsonAsync($"/api/students/{student}/adjustments", new { kind = "Discount", amount = 15000, reason = "Sibling concession" });

        var paid = await (await academy.Admin.PostAsJsonAsync("/api/payments", new { studentId = student, amount = 15000, method = "UPI" })).ReadJsonAsync();
        Assert.True(paid["emailed"]!.GetValue<bool>());
        var refund = await (await academy.Admin.PostAsJsonAsync("/api/payments/refunds", new { studentId = student, amount = 1000, method = "Cash" })).ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PostAsJsonAsync($"/api/payments/{refund["id"]}/void", new { reason = "Entered twice" })).StatusCode);

        var mails = await MailsAsync(email);
        Assert.Equal(3, mails.Count);
        Assert.All(mails, m => Assert.Equal("Receipt", m.Category));
        Assert.All(mails, m => Assert.Equal(academy.Id, m.TenantId));
        var receipt = mails[0];
        Assert.Contains("Payment received", receipt.Subject);
        Assert.Contains("Balance due: ₹30,000", receipt.Body);
        Assert.Contains("Paid to date: ₹15,000", receipt.Body);
        Assert.Contains("<table", receipt.HtmlBody);
        foreach (var mail in mails)
        {
            // Never the agreed fee, the standard fee or the discount.
            Assert.DoesNotContain("45,000", mail.Body);
            Assert.DoesNotContain("60,000", mail.Body);
            Assert.DoesNotContain("Sibling", mail.Body);
        }
        Assert.Contains("Refund", mails[1].Subject);
        Assert.Contains("cancelled", mails[2].Subject);
        Assert.Contains("Entered twice", mails[2].Body);

        // No email on record: the payment still goes through, and the office is told nothing was sent.
        var quiet = await academy.NewStudentAsync(await academy.NewCourseAsync("Quiet course"), 5000, "No Email");
        Assert.False((await (await academy.PayAsync(quiet, 1000)).ReadJsonAsync())["emailed"]!.GetValue<bool>());

        // The office receipt no longer prints the agreed fee either.
        var office = await academy.Admin.GetJsonAsync($"/api/payments/{paid["id"]}/receipt");
        Assert.Null(office["feePayable"]);
    }

    [Fact]
    public async Task Settling_writes_off_the_rest_and_the_student_is_settled()
    {
        var academy = await factory.NewAcademyAsync();
        var email = $"settle-{Guid.NewGuid():N}@example.com";
        var student = await StudentWithEmailAsync(academy, await academy.NewCourseAsync("Foundation", 45000), 45000, "Aman Verma", email);
        await academy.PayAsync(student, 40000);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await academy.Admin.PostAsJsonAsync($"/api/students/{student}/settle", new { finalPayment = 3000, method = "UPI", reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await academy.Admin.PostAsJsonAsync($"/api/students/{student}/settle", new { finalPayment = 5000, method = "UPI", reason = "Full amount" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await (await academy.NewUserAsync("Staff")).PostAsJsonAsync($"/api/students/{student}/settle", new { finalPayment = 3000, method = "UPI", reason = "x settle" })).StatusCode);

        var settled = await (await academy.Admin.PostAsJsonAsync($"/api/students/{student}/settle",
            new { finalPayment = 3000, method = "UPI", reason = "Final settlement agreed with parent" })).ReadJsonAsync();
        Assert.Equal(2000, settled.Money("writtenOff"));
        Assert.NotNull(settled["receipt"]);

        var s = await academy.StudentAsync(student);
        Assert.Equal("Settled", s["status"]!.GetValue<string>());
        Assert.Equal(0, s.Money("pending"));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await academy.Admin.PostAsJsonAsync($"/api/students/{student}/settle", new { reason = "Again settle" })).StatusCode);

        // Reminders skip settled students; the write-off is its own figure in the reports.
        Assert.Equal(0, (await (await academy.Admin.PostAsync("/api/students/reminders", null)).ReadJsonAsync())["queued"]!.GetValue<int>());
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        Assert.Equal(2000, (await academy.Admin.GetJsonAsync($"/api/reports/activity?from={today}&to={today}"))["totals"]!.Money("writtenOff"));
        Assert.Contains("Balance due: ₹0", (await MailsAsync(email)).Last().Body);

        // A write-off cannot be entered as an ordinary adjustment.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await academy.Admin.PostAsJsonAsync($"/api/students/{student}/adjustments", new { kind = "WriteOff", amount = 1, reason = "Sneaky write-off" })).StatusCode);
    }

    [Fact]
    public async Task Promoting_moves_continuing_students_and_their_logins_into_the_next_year()
    {
        var academy = await factory.NewAcademyAsync();
        var thisYear = (await academy.Admin.GetJsonAsync("/api/academic-years"))["items"]!.AsArray().Single()!;
        var start = DateOnly.Parse(thisYear["endDate"]!.GetValue<string>()).AddDays(1);
        var next = await (await academy.Admin.PostAsJsonAsync("/api/academic-years",
            new { name = "Next", startDate = start.ToString("yyyy-MM-dd"), endDate = start.AddYears(1).AddDays(-1).ToString("yyyy-MM-dd") })).ReadJsonAsync();
        var nextId = next["id"]!.GetValue<Guid>();

        var class10 = await academy.NewCourseAsync("Class 10", 40000);
        var class11 = await academy.NewCourseAsync("Class 11", 50000);
        var asha = await academy.NewStudentAsync(class10, 40000, "Asha Rao");
        var ravi = await academy.NewStudentAsync(class10, 40000, "Ravi Kumar");
        await academy.PayAsync(asha, 30000);
        var email = $"asha-{Guid.NewGuid():N}@example.com";
        await academy.Admin.PostAsJsonAsync($"/api/students/{asha}/login", new { email, password = "asha1" });

        var candidates = await academy.Admin.GetJsonAsync("/api/students/promotion");
        Assert.Equal(nextId, candidates["to"]!["id"]!.GetValue<Guid>());
        Assert.Equal(2, candidates["students"]!.AsArray().Count);

        var result = await (await academy.Admin.PostAsJsonAsync("/api/students/promote", new
        {
            fromYearId = thisYear["id"]!.GetValue<Guid>(),
            toYearId = nextId,
            students = new[] { new { studentId = asha, courseId = class11, feeAmount = 50000 } },
            markOthersInactive = true
        })).ReadJsonAsync();
        Assert.Equal(1, result["promoted"]!.GetValue<int>());
        Assert.Equal(1, result["deactivated"]!.GetValue<int>());

        // Promoting the same student twice is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await academy.Admin.PostAsJsonAsync("/api/students/promote", new
        {
            fromYearId = thisYear["id"]!.GetValue<Guid>(), toYearId = nextId,
            students = new[] { new { studentId = asha, courseId = class11, feeAmount = 50000 } }, markOthersInactive = false
        })).StatusCode);

        var newId = await factory.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(s => s.PreviousStudentId == asha).Select(s => s.Id).SingleAsync());
        var detail = await academy.Admin.GetJsonAsync($"/api/students/{newId}");
        Assert.Equal(50000, detail["student"]!.Money("feeAmount"));
        Assert.Equal(10000, detail["previousYears"]!.AsArray().Single()!.Money("due"));
        Assert.False((await academy.StudentAsync(ravi))["active"]!.GetValue<bool>());

        // Last year's balance can still be collected from the old record, which fee collection still finds.
        Assert.Contains((await academy.Admin.GetJsonAsync("/api/students/lookup?search=Asha")).AsArray(), s => s!["id"]!.GetValue<Guid>() == asha);
        Assert.Equal(HttpStatusCode.Created, (await academy.PayAsync(asha, 10000)).StatusCode);

        // The portal login followed the student into the new year.
        var portal = factory.NewBrowser();
        await portal.LoginAsync(academy.Code, email, "asha1");
        Assert.Equal("Class 11", (await portal.GetJsonAsync("/api/me/student"))["student"]!["courseName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Package_faculty_attendance_is_approved_automatically_and_summed_by_month()
    {
        var academy = await factory.NewAcademyAsync();
        await academy.Admin.PutAsJsonAsync("/api/attendance/settings", new { latitude = 28.6315, longitude = 77.2167, radiusMeters = 150 });
        var package = await FacultyPortalTests.FacultyAsync(academy, "Ritu Jain", payType: "Package");
        var portal = await FacultyPortalTests.PortalAsync(academy, package);
        object Here() => new { method = "Location", latitude = 28.6316, longitude = 77.2168, accuracy = 20 };

        Assert.Equal(HttpStatusCode.OK, (await portal.PostAsJsonAsync("/api/me/faculty/clock/in", Here())).StatusCode);
        await factory.WithDbAsync(db => db.FacultyClocks.IgnoreQueryFilters().Where(c => c.FacultyId == package && c.OutAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.InAtUtc, DateTime.UtcNow.AddMinutes(-50))));
        var done = await (await portal.PostAsJsonAsync("/api/me/faculty/clock/out", Here())).ReadJsonAsync();
        Assert.Equal("Approved", done["review"]!.GetValue<string>());

        // Switched off, the same class would wait for approval.
        await academy.Admin.PutAsJsonAsync("/api/attendance/settings", new { latitude = 28.6315, longitude = 77.2167, radiusMeters = 150, autoApprovePackageClasses = false });
        await portal.PostAsJsonAsync("/api/me/faculty/clock/in", Here());
        await factory.WithDbAsync(db => db.FacultyClocks.IgnoreQueryFilters().Where(c => c.FacultyId == package && c.OutAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.InAtUtc, DateTime.UtcNow.AddMinutes(-30))));
        Assert.Equal("Pending", (await (await portal.PostAsJsonAsync("/api/me/faculty/clock/out", Here())).ReadJsonAsync())["review"]!.GetValue<string>());

        var month = (await academy.Admin.GetJsonAsync("/api/faculty/attendance"))["items"]!.AsArray().Single()!;
        Assert.Equal(1, month["classes"]!.GetValue<int>());
        Assert.Equal(50, month["minutes"]!.GetValue<int>());
        Assert.Equal(1, month["days"]!.GetValue<int>());
        Assert.Equal(1, month["waiting"]!.GetValue<int>());
        Assert.Equal(1, (await portal.GetJsonAsync("/api/me/faculty"))["thisMonth"]!["days"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.GetAsync("/api/faculty/attendance/excel")).StatusCode);
    }
}
