using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Academy.Api.Features;
using Academy.Api.Tests.Support;

namespace Academy.Api.Tests;

[Collection(AppCollection.Name)]
public sealed class FacultyPortalTests(AppFactory factory)
{
    private static async Task<Guid> FacultyAsync(TestAcademy academy, string name = "Navin Saw", string payType = "Hourly", decimal rate = 600)
    {
        var response = await academy.Admin.PostAsJsonAsync("/api/faculty", new { fullName = name, payType, hourlyRate = rate, agreedPayout = 50000 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync())["id"]!.GetValue<Guid>();
    }

    /// <summary>Gives the faculty member a portal login and signs in with it.</summary>
    private static async Task<HttpClient> PortalAsync(TestAcademy academy, Guid facultyId)
    {
        var email = $"faculty-{Guid.NewGuid():N}@{academy.Code}.test";
        var created = await academy.Admin.PostAsJsonAsync($"/api/faculty/{facultyId}/login", new { email, password = AppFactory.Password });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var client = academy.Factory.NewBrowser();
        var session = await client.LoginAsync(academy.Code, email);
        Assert.Equal("Faculty", session["user"]!["role"]!.GetValue<string>());
        return client;
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient portal, string start, string end, string? date = null) =>
        portal.PostAsJsonAsync("/api/me/faculty/sessions", new { taughtOn = date ?? DateTime.UtcNow.ToString("yyyy-MM-dd"), startTime = start, endTime = end, topic = "Algebra" });

    private static async Task<Guid> SubmittedIdAsync(HttpClient portal, string start, string end)
    {
        var response = await SubmitAsync(portal, start, end);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync())["id"]!.GetValue<Guid>();
    }

    private static async Task<JsonNode> FacultyRowAsync(TestAcademy academy, Guid id) =>
        (await academy.Admin.GetJsonAsync("/api/faculty")).AsArray().Single(f => f!["id"]!.GetValue<Guid>() == id)!;

    [Fact]
    public async Task A_faculty_login_reaches_only_its_own_portal()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyAsync(academy);
        var portal = await PortalAsync(academy, facultyId);

        Assert.Equal("Navin Saw", (await portal.GetJsonAsync("/api/me/faculty"))["faculty"]!["fullName"]!.GetValue<string>());
        foreach (var url in new[] { "/api/dashboard", "/api/students", "/api/faculty", "/api/payouts", "/api/reports/summary", "/api/academic-years", "/api/users" })
            Assert.Equal(HttpStatusCode.Forbidden, (await portal.GetAsync(url)).StatusCode);

        // Staff do not have a faculty portal.
        Assert.Equal(HttpStatusCode.Forbidden, (await academy.Admin.GetAsync("/api/me/faculty")).StatusCode);

        var row = await FacultyRowAsync(academy, facultyId);
        Assert.NotNull(row["login"]);
        // One login per faculty member, and the Users screen cannot make faculty logins.
        Assert.Equal(HttpStatusCode.Conflict,
            (await academy.Admin.PostAsJsonAsync($"/api/faculty/{facultyId}/login", new { email = $"x-{Guid.NewGuid():N}@a.test", password = AppFactory.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await academy.Admin.PostAsJsonAsync("/api/users", new { displayName = "T", email = $"t-{Guid.NewGuid():N}@a.test", role = "Faculty", password = AppFactory.Password })).StatusCode);
    }

    [Fact]
    public async Task Submitted_time_counts_only_once_approved_and_cannot_overlap()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyAsync(academy, rate: 600);
        var portal = await PortalAsync(academy, facultyId);

        var first = await (await SubmitAsync(portal, "10:00", "11:25")).ReadJsonAsync();
        Assert.Equal(85, first["minutes"]!.GetValue<int>());
        Assert.Equal(850, first.Money("amount"));

        // Waiting for approval: nothing is owed yet.
        Assert.Equal(0, (await FacultyRowAsync(academy, facultyId)).Money("earned"));
        Assert.Equal(1, (await FacultyRowAsync(academy, facultyId))["pendingSessions"]!.GetValue<int>());

        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitAsync(portal, "11:00", "12:00")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitAsync(portal, "12:00", "11:00")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitAsync(portal, "09:00", "10:00", DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"))).StatusCode);
        var second = await SubmittedIdAsync(portal, "11:25", "12:55");

        var firstId = first["id"]!.GetValue<Guid>();
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PostAsync($"/api/faculty-sessions/{firstId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await academy.Admin.PostAsync($"/api/faculty-sessions/{firstId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await academy.Admin.PostAsJsonAsync($"/api/faculty-sessions/{second}/reject", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PostAsJsonAsync($"/api/faculty-sessions/{second}/reject", new { reason = "Class was cancelled" })).StatusCode);

        Assert.Equal(850, (await FacultyRowAsync(academy, facultyId)).Money("earned"));
        Assert.Equal(850, (await academy.Admin.GetJsonAsync("/api/dashboard"))["finance"]!.Money("facultyPayable"));

        // The rejected slot is free again.
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(portal, "11:30", "12:00")).StatusCode);
        var removed = await portal.GetJsonAsync("/api/me/faculty/sessions?view=removed");
        Assert.Equal("Class was cancelled", removed["items"]!.AsArray().Single()!["note"]!.GetValue<string>());
    }

    [Fact]
    public async Task Payouts_mark_the_oldest_approved_sessions_paid()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyAsync(academy, rate: 600);
        var portal = await PortalAsync(academy, facultyId);
        foreach (var id in new[] { await SubmittedIdAsync(portal, "08:00", "09:00"), await SubmittedIdAsync(portal, "09:00", "10:00") })
            await academy.Admin.PostAsync($"/api/faculty-sessions/{id}/approve", null);
        await SubmittedIdAsync(portal, "10:00", "10:30");

        Assert.Equal(HttpStatusCode.Created, (await academy.Admin.PostAsJsonAsync("/api/payouts", new { facultyId, amount = 600, method = "UPI" })).StatusCode);

        var sessions = await portal.GetJsonAsync("/api/me/faculty/sessions");
        Assert.Equal(3, sessions["counts"]!["all"]!.GetValue<int>());
        Assert.Equal(1, sessions["counts"]!["paid"]!.GetValue<int>());
        Assert.Equal(1, sessions["counts"]!["approved"]!.GetValue<int>());
        Assert.Equal(1, sessions["counts"]!["waiting"]!.GetValue<int>());

        var payments = await portal.GetJsonAsync("/api/me/faculty/payments");
        Assert.Equal(600, payments.Money("unpaid"));
        Assert.Equal(600, payments.Money("received"));
        Assert.Equal(300, payments.Money("waitingAmount"));
        Assert.Equal(600, payments["rates"]!.AsArray().Single()!.Money("hourlyRate"));

        var overview = await portal.GetJsonAsync("/api/me/faculty");
        Assert.Equal(120, overview["thisMonth"]!["minutes"]!.GetValue<int>());
        Assert.Equal(1, overview["totals"]!["unpaidSessions"]!.GetValue<int>());
    }

    [Fact]
    public async Task Only_waiting_sessions_can_be_withdrawn_and_only_by_their_owner()
    {
        var academy = await factory.NewAcademyAsync();
        var portal = await PortalAsync(academy, await FacultyAsync(academy));
        var other = await PortalAsync(academy, await FacultyAsync(academy, "Ritu Jain"));
        var waiting = await SubmittedIdAsync(portal, "14:00", "15:00");
        var approved = await SubmittedIdAsync(portal, "15:00", "16:00");
        await academy.Admin.PostAsync($"/api/faculty-sessions/{approved}/approve", null);

        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/me/faculty/sessions/{waiting}/withdraw", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await portal.PostAsync($"/api/me/faculty/sessions/{approved}/withdraw", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await portal.PostAsync($"/api/me/faculty/sessions/{waiting}/withdraw", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await academy.Admin.PostAsync($"/api/faculty-sessions/{waiting}/approve", null)).StatusCode);
        // The other faculty member sees none of this.
        Assert.Equal(0, (await other.GetJsonAsync("/api/me/faculty/sessions"))["counts"]!["all"]!.GetValue<int>());
    }

    [Fact]
    public async Task Package_faculty_see_their_pay_but_do_not_log_time()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyAsync(academy, payType: "Package");
        var portal = await PortalAsync(academy, facultyId);
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitAsync(portal, "10:00", "11:00")).StatusCode);

        await academy.Admin.PostAsJsonAsync("/api/payouts", new { facultyId, amount = 20000, method = "Bank Transfer" });
        var payments = await portal.GetJsonAsync("/api/me/faculty/payments");
        Assert.Equal(30000, payments.Money("unpaid"));
        Assert.Equal(20000, payments.Money("received"));
    }
}

[Collection(AppCollection.Name)]
public sealed class PeriodReportTests(AppFactory factory)
{
    [Fact]
    public void Weeks_run_monday_to_sunday_and_are_clipped_to_the_range()
    {
        var periods = ReportPeriods.Split(new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 30), ReportGrouping.Week);
        Assert.Equal(
            [(new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 22)), (new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 29)), (new DateOnly(2026, 11, 30), new DateOnly(2026, 11, 30))],
            periods.Select(p => (p.From, p.To)));
        Assert.Equal(3, ReportPeriods.Split(new DateOnly(2026, 11, 20), new DateOnly(2027, 1, 5), ReportGrouping.Month).Count);
        Assert.Equal(13, ReportPeriods.Split(new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 30), ReportGrouping.Day).Count);
    }

    [Fact]
    public async Task The_summary_groups_by_day_week_or_month_and_downloads_as_excel()
    {
        var academy = await factory.NewAcademyAsync();
        var student = await academy.NewStudentAsync(await academy.NewCourseAsync());
        await academy.Admin.PostAsJsonAsync("/api/payments", new { studentId = student, amount = 1000, method = "Cash", paidOn = "2026-11-02" });
        await academy.Admin.PostAsJsonAsync("/api/payments", new { studentId = student, amount = 500, method = "UPI", paidOn = "2026-11-04" });

        var byDay = await academy.Admin.GetJsonAsync("/api/reports/summary?from=2026-11-01&to=2026-11-07&groupBy=day");
        var days = byDay["periods"]!.AsArray();
        Assert.Equal(7, days.Count);
        Assert.Equal(1000, days[1]!.Money("collections"));
        Assert.Equal(500, days[3]!.Money("collections"));
        Assert.Equal("Day by day", byDay["title"]!.GetValue<string>());

        // 1 Nov 2026 is a Sunday: the end of one week, then Monday 2 Nov starts the next.
        var byWeek = await academy.Admin.GetJsonAsync("/api/reports/summary?from=2026-11-01&to=2026-11-07&groupBy=week");
        Assert.Equal([0m, 1500m], byWeek["periods"]!.AsArray().Select(p => p!.Money("collections")));

        Assert.Equal(HttpStatusCode.BadRequest, (await academy.Admin.GetAsync("/api/reports/summary?from=2025-01-01&to=2026-11-07&groupBy=day")).StatusCode);

        foreach (var url in new[] { "/api/reports/summary/excel?from=2026-11-01&to=2026-11-07&groupBy=week", "/api/reports/export/payments?from=2026-11-01&to=2026-11-07" })
        {
            var file = await academy.Admin.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            Assert.Equal(Excel.ContentType, file.Content.Headers.ContentType!.MediaType);
            var bytes = await file.Content.ReadAsByteArrayAsync();
            Assert.Equal((byte)'P', bytes[0]);
            Assert.Equal((byte)'K', bytes[1]);
        }
    }

    [Fact]
    public async Task The_activity_report_lists_what_happened_and_is_for_the_super_admin()
    {
        var academy = await factory.NewAcademyAsync();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var student = await academy.NewStudentAsync(await academy.NewCourseAsync(), name: "Kabir Mehta");
        await academy.PayAsync(student, 2500);
        await academy.Admin.PostAsJsonAsync("/api/expenses", new { title = "Printer ink", amount = 400, category = "Supplies", method = "Cash" });

        var report = await academy.Admin.GetJsonAsync($"/api/reports/activity?from={today}&to={today}&groupBy=day");
        var types = report["events"]!.AsArray().Select(e => e!["type"]!.GetValue<string>()).ToList();
        Assert.Contains("Admission", types);
        Assert.Contains("Fee payment", types);
        Assert.Contains("Expense", types);
        Assert.Equal(1, report["totals"]!["admissions"]!.GetValue<int>());
        Assert.Equal(2500, report["totals"]!.Money("collections"));
        Assert.Equal(2100, report["totals"]!.Money("netCash"));

        var excel = await academy.Admin.GetAsync($"/api/reports/activity/excel?from={today}&to={today}&groupBy=day");
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        Assert.Equal(Excel.ContentType, excel.Content.Headers.ContentType!.MediaType);

        // Admins and accountants have the normal reports but not the activity report.
        foreach (var role in new[] { "Admin", "Accountant" })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await academy.NewUserAsync(role)).GetAsync("/api/reports/activity")).StatusCode);
    }
}
