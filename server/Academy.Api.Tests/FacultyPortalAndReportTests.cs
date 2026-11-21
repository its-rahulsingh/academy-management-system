using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Academy.Api.Domain;
using Academy.Api.Features;
using Academy.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Tests;

[Collection(AppCollection.Name)]
public sealed class FacultyPortalTests(AppFactory factory)
{
    // The academy is at Connaught Place, New Delhi. ~45 m and ~3 km away for the location tests.
    private const double AcademyLat = 28.6315, AcademyLon = 77.2167;
    private const double NearLat = 28.6319, NearLon = 77.2167;
    private const double FarLat = 28.6585, FarLon = 77.2167;

    internal static async Task<Guid> FacultyAsync(TestAcademy academy, string name = "Navin Saw", string payType = "Hourly", decimal rate = 600)
    {
        var response = await academy.Admin.PostAsJsonAsync("/api/faculty", new { fullName = name, payType, hourlyRate = rate, agreedPayout = 50000 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync())["id"]!.GetValue<Guid>();
    }

    /// <summary>Gives the faculty member a portal login and signs in with it.</summary>
    internal static async Task<HttpClient> PortalAsync(TestAcademy academy, Guid facultyId)
    {
        var email = $"faculty-{Guid.NewGuid():N}@{academy.Code}.test";
        var created = await academy.Admin.PostAsJsonAsync($"/api/faculty/{facultyId}/login", new { email, password = AppFactory.Password });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var client = academy.Factory.NewBrowser();
        var session = await client.LoginAsync(academy.Code, email);
        Assert.Equal("Faculty", session["user"]!["role"]!.GetValue<string>());
        return client;
    }

    private static async Task<string> QrCodeAsync(TestAcademy academy) =>
        (await academy.Admin.GetJsonAsync("/api/attendance/qr"))["code"]!.GetValue<string>();

    private static async Task<HttpResponseMessage> ClockInByQrAsync(TestAcademy academy, HttpClient portal) =>
        await portal.PostAsJsonAsync("/api/me/faculty/clock/in", new { method = "Qr", code = await QrCodeAsync(academy) });

    private static async Task<HttpResponseMessage> ClockOutByQrAsync(TestAcademy academy, HttpClient portal, string topic = "Algebra") =>
        await portal.PostAsJsonAsync("/api/me/faculty/clock/out", new { method = "Qr", code = await QrCodeAsync(academy), topic });

    /// <summary>Moves the faculty member's running class back in time, as if it started that many minutes ago.</summary>
    private Task StartedMinutesAgoAsync(Guid facultyId, int minutes) =>
        factory.WithDbAsync(db => db.FacultyClocks.IgnoreQueryFilters()
            .Where(c => c.FacultyId == facultyId && c.OutAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.InAtUtc, DateTime.UtcNow.AddMinutes(-minutes))));

    private static async Task<JsonNode> FacultyRowAsync(TestAcademy academy, Guid id) =>
        (await academy.Admin.GetJsonAsync("/api/faculty")).AsArray().Single(f => f!["id"]!.GetValue<Guid>() == id)!;

    private static Task SetLocationAsync(TestAcademy academy, int radius = 150) =>
        academy.Admin.PutAsJsonAsync("/api/attendance/settings", new { latitude = AcademyLat, longitude = AcademyLon, radiusMeters = radius });

    [Fact]
    public async Task A_faculty_login_reaches_only_its_own_portal()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyAsync(academy);
        var portal = await PortalAsync(academy, facultyId);

        Assert.Equal("Navin Saw", (await portal.GetJsonAsync("/api/me/faculty"))["faculty"]!["fullName"]!.GetValue<string>());
        foreach (var url in new[] { "/api/dashboard", "/api/students", "/api/faculty", "/api/payouts", "/api/reports/summary", "/api/academic-years", "/api/users", "/api/attendance/qr", "/api/attendance/live", "/api/me/student" })
            Assert.Equal(HttpStatusCode.Forbidden, (await portal.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await academy.Admin.GetAsync("/api/me/faculty")).StatusCode);

        Assert.NotNull((await FacultyRowAsync(academy, facultyId))["login"]);
        Assert.Equal(HttpStatusCode.Conflict,
            (await academy.Admin.PostAsJsonAsync($"/api/faculty/{facultyId}/login", new { email = $"x-{Guid.NewGuid():N}@a.test", password = AppFactory.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await academy.Admin.PostAsJsonAsync("/api/users", new { displayName = "T", email = $"t-{Guid.NewGuid():N}@a.test", role = "Faculty", password = AppFactory.Password })).StatusCode);
    }

    [Fact]
    public async Task A_class_clocked_in_and_out_by_qr_is_timed_by_the_server_and_approved_at_once()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyAsync(academy, rate: 600);
        var portal = await PortalAsync(academy, facultyId);

        Assert.Equal(HttpStatusCode.OK, (await ClockInByQrAsync(academy, portal)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ClockInByQrAsync(academy, portal)).StatusCode);
        var status = await portal.GetJsonAsync("/api/me/faculty/clock");
        Assert.Equal("Qr", status["running"]!["inMethod"]!.GetValue<string>());

        // Admins see who is teaching right now.
        var live = (await academy.Admin.GetJsonAsync("/api/attendance/live"))["items"]!.AsArray();
        Assert.Equal("Navin Saw", live.Single()!["facultyName"]!.GetValue<string>());

        await StartedMinutesAgoAsync(facultyId, 85);
        var done = await (await ClockOutByQrAsync(academy, portal)).ReadJsonAsync();
        Assert.Equal(85, done["minutes"]!.GetValue<int>());
        Assert.Equal(850, done.Money("amount"));
        Assert.Equal("Approved", done["review"]!.GetValue<string>());

        Assert.Equal(850, (await FacultyRowAsync(academy, facultyId)).Money("earned"));
        Assert.Empty((await academy.Admin.GetJsonAsync("/api/attendance/live"))["items"]!.AsArray());
        Assert.Equal(HttpStatusCode.Conflict, (await ClockOutByQrAsync(academy, portal)).StatusCode);
    }

    [Fact]
    public async Task Clocking_in_by_location_works_only_near_the_academy_and_waits_for_approval()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyAsync(academy, rate: 600);
        var portal = await PortalAsync(academy, facultyId);
        object Here(double lat, double lon) => new { method = "Location", latitude = lat, longitude = lon, accuracy = 20 };

        // No academy location yet: only QR works.
        Assert.Equal(HttpStatusCode.BadRequest, (await portal.PostAsJsonAsync("/api/me/faculty/clock/in", Here(NearLat, NearLon))).StatusCode);
        await SetLocationAsync(academy);

        var far = await portal.PostAsJsonAsync("/api/me/faculty/clock/in", Here(FarLat, FarLon));
        Assert.Equal(HttpStatusCode.BadRequest, far.StatusCode);
        Assert.Contains("km from the academy", await far.ErrorAsync());
        Assert.Equal(HttpStatusCode.OK, (await portal.PostAsJsonAsync("/api/me/faculty/clock/in", Here(NearLat, NearLon))).StatusCode);

        await StartedMinutesAgoAsync(facultyId, 60);
        var done = await (await portal.PostAsJsonAsync("/api/me/faculty/clock/out", Here(NearLat, NearLon))).ReadJsonAsync();
        Assert.Equal("Pending", done["review"]!.GetValue<string>());
        Assert.Equal(0, (await FacultyRowAsync(academy, facultyId)).Money("earned"));

        var waiting = (await academy.Admin.GetJsonAsync("/api/faculty-sessions?review=Pending"))["items"]!.AsArray().Single()!;
        Assert.Equal("Location", waiting["clock"]!["inMethod"]!.GetValue<string>());
        Assert.True(waiting["clock"]!["inDistanceMeters"]!.GetValue<double>() < 100);
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PostAsync($"/api/faculty-sessions/{waiting["id"]}/approve", null)).StatusCode);
        Assert.Equal(600, (await FacultyRowAsync(academy, facultyId)).Money("earned"));
    }

    [Fact]
    public async Task Under_a_minute_is_discarded_and_admins_can_stop_a_forgotten_class()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyAsync(academy);
        var portal = await PortalAsync(academy, facultyId);

        await ClockInByQrAsync(academy, portal);
        Assert.True((await (await ClockOutByQrAsync(academy, portal)).ReadJsonAsync())["cancelled"]!.GetValue<bool>());

        await ClockInByQrAsync(academy, portal);
        await StartedMinutesAgoAsync(facultyId, 45);
        var running = (await academy.Admin.GetJsonAsync("/api/attendance/live"))["items"]!.AsArray().Single()!;
        var stopped = await (await academy.Admin.PostAsJsonAsync($"/api/attendance/{running["id"]}/stop", new { })).ReadJsonAsync();
        Assert.Equal(45, stopped["minutes"]!.GetValue<int>());
        Assert.Equal("Pending", stopped["review"]!.GetValue<string>());
        Assert.Null((await portal.GetJsonAsync("/api/me/faculty/clock"))["running"]);
    }

    [Fact]
    public async Task Check_in_codes_expire_and_cannot_be_forged_or_used_at_another_academy()
    {
        var secret = CheckInCodes.NewSecret();
        var tenant = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var code = CheckInCodes.Issue(secret, tenant, admin, now);

        Assert.Equal(admin, CheckInCodes.Verify(secret, tenant, code, now.AddSeconds(30)).IssuerId);
        Assert.NotNull(CheckInCodes.Verify(secret, tenant, code, now.AddSeconds(90)).Error);
        Assert.NotNull(CheckInCodes.Verify(secret, Guid.NewGuid(), code, now).Error);
        Assert.NotNull(CheckInCodes.Verify(CheckInCodes.NewSecret(), tenant, code, now).Error);
        Assert.NotNull(CheckInCodes.Verify(secret, tenant, code[..^2] + (code[^2] == 'A' ? "BA" : "AA"), now).Error);
        Assert.NotNull(CheckInCodes.Verify(secret, tenant, "not-a-code", now).Error);

        // Another academy's real code is refused too.
        var one = await factory.NewAcademyAsync();
        var two = await factory.NewAcademyAsync();
        var portal = await PortalAsync(one, await FacultyAsync(one));
        var response = await portal.PostAsJsonAsync("/api/me/faculty/clock/in", new { method = "Qr", code = await QrCodeAsync(two) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Payouts_mark_the_oldest_approved_sessions_paid()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyAsync(academy, rate: 600);
        var portal = await PortalAsync(academy, facultyId);
        foreach (var minutes in new[] { 60, 60 })
            await academy.Admin.PostAsJsonAsync("/api/faculty-sessions", new { facultyId, minutes });

        Assert.Equal(HttpStatusCode.Created, (await academy.Admin.PostAsJsonAsync("/api/payouts", new { facultyId, amount = 600, method = "UPI" })).StatusCode);

        var sessions = await portal.GetJsonAsync("/api/me/faculty/sessions");
        Assert.Equal(1, sessions["counts"]!["paid"]!.GetValue<int>());
        Assert.Equal(1, sessions["counts"]!["approved"]!.GetValue<int>());
        var payments = await portal.GetJsonAsync("/api/me/faculty/payments");
        Assert.Equal(600, payments.Money("unpaid"));
        Assert.Equal(600, payments.Money("received"));
    }

    [Fact]
    public async Task Package_faculty_clock_in_for_attendance_without_pay()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyAsync(academy, payType: "Package");
        var portal = await PortalAsync(academy, facultyId);
        await ClockInByQrAsync(academy, portal);
        await StartedMinutesAgoAsync(facultyId, 50);
        var done = await (await ClockOutByQrAsync(academy, portal)).ReadJsonAsync();
        Assert.Equal(50, done["minutes"]!.GetValue<int>());
        Assert.Equal(0, done.Money("amount"));
        Assert.Equal(50000, (await FacultyRowAsync(academy, facultyId)).Money("earned"));
    }
}

[Collection(AppCollection.Name)]
public sealed class StudentPortalTests(AppFactory factory)
{
    [Fact]
    public async Task A_student_sees_only_their_own_fees_receipts_and_results()
    {
        var academy = await factory.NewAcademyAsync();
        var course = await academy.NewCourseAsync("Class 10", 20000);
        var asha = await academy.NewStudentAsync(course, 20000, "Asha Rao");
        var ravi = await academy.NewStudentAsync(course, 20000, "Ravi Kumar");
        var ashaPayment = (await (await academy.PayAsync(asha, 5000)).ReadJsonAsync())["id"]!.GetValue<Guid>();
        var raviPayment = (await (await academy.PayAsync(ravi, 7000)).ReadJsonAsync())["id"]!.GetValue<Guid>();

        var email = $"asha-{Guid.NewGuid():N}@{academy.Code}.test";
        Assert.Equal(HttpStatusCode.Created, (await academy.Admin.PostAsJsonAsync($"/api/students/{asha}/login", new { email, password = "asha1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await academy.Admin.PostAsJsonAsync($"/api/students/{asha}/login", new { email = "x" + email, password = "asha1" })).StatusCode);
        Assert.Equal(email, (await academy.Admin.GetJsonAsync($"/api/students/{asha}"))["login"]!["email"]!.GetValue<string>());

        var portal = factory.NewBrowser();
        Assert.Equal("Student", (await portal.LoginAsync(academy.Code, email, "asha1"))["user"]!["role"]!.GetValue<string>());

        var me = await portal.GetJsonAsync("/api/me/student");
        Assert.Equal("Asha Rao", me["student"]!["fullName"]!.GetValue<string>());
        Assert.Equal(15000, me["fees"]!.Money("pending"));
        Assert.Single(me["payments"]!.AsArray());

        Assert.Equal(HttpStatusCode.OK, (await portal.GetAsync($"/api/me/student/receipts/{ashaPayment}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync($"/api/me/student/receipts/{raviPayment}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await portal.GetAsync("/api/me/student/report-card")).StatusCode);

        foreach (var url in new[] { "/api/dashboard", "/api/students", $"/api/payments/{raviPayment}/receipt", $"/api/tests/report-cards/{ravi}", "/api/me/faculty" })
            Assert.Equal(HttpStatusCode.Forbidden, (await portal.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Passwords_need_at_least_five_characters()
    {
        var academy = await factory.NewAcademyAsync();
        var make = (string password) => academy.Admin.PostAsJsonAsync("/api/users",
            new { displayName = "Desk", email = $"d-{Guid.NewGuid():N}@a.test", role = "Staff", password });
        Assert.Equal(HttpStatusCode.BadRequest, (await make("abcd")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await make("abcde")).StatusCode);
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
