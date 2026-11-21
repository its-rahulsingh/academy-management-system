using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Academy.Api.Domain;
using Academy.Api.Features;
using Academy.Api.Infrastructure.Email;
using Academy.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Tests;

/// <summary>What happens when a faculty member forgets to clock in or out, and the academy's own email identity.</summary>
[Collection(AppCollection.Name)]
public sealed class ForgottenClockTests(AppFactory factory)
{
    private static async Task<string> QrAsync(TestAcademy academy) =>
        (await academy.Admin.GetJsonAsync("/api/attendance/qr"))["code"]!.GetValue<string>();

    private static async Task ClockInAsync(TestAcademy academy, HttpClient portal) =>
        Assert.Equal(HttpStatusCode.OK, (await portal.PostAsJsonAsync("/api/me/faculty/clock/in", new { method = "Qr", code = await QrAsync(academy) })).StatusCode);

    private static async Task<JsonNode> ClockOutAsync(TestAcademy academy, HttpClient portal, string? endTime = null) =>
        await (await portal.PostAsJsonAsync("/api/me/faculty/clock/out", new { method = "Qr", code = await QrAsync(academy), endTime })).ReadJsonAsync();

    private Task SetStartAsync(Guid facultyId, DateTime inAtUtc) =>
        factory.WithDbAsync(db => db.FacultyClocks.IgnoreQueryFilters()
            .Where(c => c.FacultyId == facultyId && c.OutAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.InAtUtc, inAtUtc)));

    private static string LocalClock(DateTime utc) => AcademyClock.ToLocal(utc).ToString("HH:mm");

    private static async Task<JsonNode> PendingAsync(TestAcademy academy) =>
        (await academy.Admin.GetJsonAsync("/api/faculty-sessions?review=Pending"))["items"]!.AsArray().Single()!;

    [Fact]
    public async Task A_class_longer_than_the_academy_maximum_waits_for_approval_even_by_qr()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyPortalTests.FacultyAsync(academy);
        var portal = await FacultyPortalTests.PortalAsync(academy, facultyId);

        await ClockInAsync(academy, portal);
        await SetStartAsync(facultyId, DateTime.UtcNow.AddMinutes(-200));
        var done = await ClockOutAsync(academy, portal);
        Assert.Equal(200, done["minutes"]!.GetValue<int>());
        Assert.Equal("Pending", done["review"]!.GetValue<string>());
        Assert.Contains("maximum class length", (await PendingAsync(academy))["reviewNote"]!.GetValue<string>());

        // The academy can change its maximum.
        Assert.Equal(HttpStatusCode.BadRequest, (await academy.Admin.PutAsJsonAsync("/api/attendance/settings", new { radiusMeters = 150, maxClassMinutes = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PutAsJsonAsync("/api/attendance/settings", new { radiusMeters = 150, maxClassMinutes = 240 })).StatusCode);
        await ClockInAsync(academy, portal);
        await SetStartAsync(facultyId, DateTime.UtcNow.AddMinutes(-200));
        Assert.Equal("Approved", (await ClockOutAsync(academy, portal))["review"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_late_clock_out_can_give_the_real_end_time_and_then_needs_approval()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyPortalTests.FacultyAsync(academy, rate: 600);
        var portal = await FacultyPortalTests.PortalAsync(academy, facultyId);
        var started = DateTime.UtcNow.AddMinutes(-150);

        await ClockInAsync(academy, portal);
        await SetStartAsync(facultyId, started);
        var badEnd = await portal.PostAsJsonAsync("/api/me/faculty/clock/out", new { method = "Qr", code = await QrAsync(academy), endTime = LocalClock(DateTime.UtcNow.AddMinutes(30)) });
        Assert.Equal(HttpStatusCode.BadRequest, badEnd.StatusCode);

        var done = await ClockOutAsync(academy, portal, LocalClock(started.AddMinutes(61)));
        Assert.InRange(done["minutes"]!.GetValue<int>(), 60, 61);
        Assert.Equal("Pending", done["review"]!.GetValue<string>());
        Assert.Contains("End time entered by the faculty member", (await PendingAsync(academy))["reviewNote"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_admin_stopping_a_class_can_say_when_it_really_ended()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyPortalTests.FacultyAsync(academy);
        var portal = await FacultyPortalTests.PortalAsync(academy, facultyId);
        var started = DateTime.UtcNow.AddMinutes(-240);

        await ClockInAsync(academy, portal);
        await SetStartAsync(facultyId, started);
        var running = (await academy.Admin.GetJsonAsync("/api/attendance/live"))["items"]!.AsArray().Single()!;
        Assert.True(running["overdue"]!.GetValue<bool>());

        var stopped = await (await academy.Admin.PostAsJsonAsync($"/api/attendance/{running["id"]}/stop", new { endTime = LocalClock(started.AddMinutes(91)) })).ReadJsonAsync();
        Assert.InRange(stopped["minutes"]!.GetValue<int>(), 90, 91);
        Assert.Equal("Pending", stopped["review"]!.GetValue<string>());
        Assert.Contains("Stopped by", (await PendingAsync(academy))["reviewNote"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_forgotten_clock_in_is_requested_with_a_reason_and_always_needs_approval()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyPortalTests.FacultyAsync(academy, rate: 600);
        var portal = await FacultyPortalTests.PortalAsync(academy, facultyId);
        var yesterday = AcademyClock.LocalDate(DateTime.UtcNow).AddDays(-1).ToString("yyyy-MM-dd");
        object Ask(string start, string end, string reason, string date) => new { date, startTime = start, endTime = end, reason, topic = "Algebra" };

        Assert.Equal(HttpStatusCode.BadRequest, (await portal.PostAsJsonAsync("/api/me/faculty/clock/request", Ask("10:00", "11:30", "", yesterday))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await portal.PostAsJsonAsync("/api/me/faculty/clock/request", Ask("11:30", "10:00", "Phone was dead", yesterday))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await portal.PostAsJsonAsync("/api/me/faculty/clock/request", Ask("10:00", "11:00", "Phone was dead", AcademyClock.LocalDate(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd")))).StatusCode);

        var done = await (await portal.PostAsJsonAsync("/api/me/faculty/clock/request", Ask("10:00", "11:30", "Phone was dead", yesterday))).ReadJsonAsync();
        Assert.Equal(90, done["minutes"]!.GetValue<int>());
        Assert.Equal(900, done.Money("amount"));
        Assert.Equal("Pending", done["review"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await portal.PostAsJsonAsync("/api/me/faculty/clock/request", Ask("11:00", "12:00", "Again", yesterday))).StatusCode);

        var pending = await PendingAsync(academy);
        Assert.Equal("Manual", pending["clock"]!["inMethod"]!.GetValue<string>());
        Assert.Contains("Phone was dead", pending["reviewNote"]!.GetValue<string>());
    }

    [Fact]
    public async Task Forgotten_classes_get_a_reminder_then_close_at_midnight_without_pay()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = await FacultyPortalTests.FacultyAsync(academy, rate: 600);
        var portal = await FacultyPortalTests.PortalAsync(academy, facultyId);
        var today = AcademyClock.LocalDate(DateTime.UtcNow);
        var midnight = AcademyClock.UtcStartOf(today);

        // Started 00:30, still running at 04:00 the same day: past the 3-hour maximum, so one reminder.
        await ClockInAsync(academy, portal);
        await SetStartAsync(facultyId, midnight.AddMinutes(30));
        var options = new AppOptions { PublicBaseUrl = "https://academy.test" };
        await factory.WithDbAsync(db => ClassWatchService.RunAsync(db, options, midnight.AddHours(4), default));
        await factory.WithDbAsync(db => ClassWatchService.RunAsync(db, options, midnight.AddHours(4).AddMinutes(5), default));
        var reminders = await factory.WithDbAsync(db => db.EmailMessages.IgnoreQueryFilters()
            .CountAsync(m => m.TenantId == academy.Id && m.Category == "ClassReminder"));
        Assert.Equal(1, reminders);

        // Started yesterday and never ended: closed at midnight, no pay, and the next clock-in is allowed.
        await SetStartAsync(facultyId, midnight.AddDays(-1).AddHours(10));
        await factory.WithDbAsync(db => ClassWatchService.RunAsync(db, options, midnight.AddMinutes(20), default));
        var status = await portal.GetJsonAsync("/api/me/faculty/clock");
        Assert.Null(status["running"]);
        var forgotten = status["needsEndTime"]!.AsArray().Single()!;
        Assert.Single((await academy.Admin.GetJsonAsync("/api/attendance/live"))["needsEndTime"]!.AsArray());
        Assert.Equal(0, (await academy.Admin.GetJsonAsync("/api/faculty")).AsArray().Single()!.Money("earned"));
        await ClockInAsync(academy, portal);

        // Entering when it really ended creates the session, for approval.
        var ended = await (await portal.PostAsJsonAsync($"/api/me/faculty/clock/{forgotten["id"]}/end", new { endTime = "11:15" })).ReadJsonAsync();
        Assert.Equal(75, ended["minutes"]!.GetValue<int>());
        Assert.Equal("Pending", ended["review"]!.GetValue<string>());
        Assert.Empty((await portal.GetJsonAsync("/api/me/faculty/clock"))["needsEndTime"]!.AsArray());
    }

    [Fact]
    public async Task Each_academy_sets_the_name_its_emails_show_and_where_replies_go()
    {
        var academy = await factory.NewAcademyAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await academy.Admin.PutAsJsonAsync("/api/branding/email", new { senderName = "Riverside", replyTo = "not-an-email" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PutAsJsonAsync("/api/branding/email", new { senderName = "Riverside \"Tutors\"", replyTo = "Office@Riverside.test" })).StatusCode);

        var branding = await academy.Admin.GetJsonAsync("/api/branding");
        Assert.Equal("Riverside Tutors", branding["emailSenderName"]!.GetValue<string>());
        Assert.Equal("office@riverside.test", branding["replyToEmail"]!.GetValue<string>());

        var sender = await factory.WithDbAsync(db => EmailSender.ForTenantsAsync(db, [academy.Id], default));
        Assert.Equal(new EmailSender("Riverside Tutors", "office@riverside.test"), sender[academy.Id]);

        // With no sender name, the academy's own name is used.
        await academy.Admin.PutAsJsonAsync("/api/branding/email", new { senderName = "", replyTo = "" });
        sender = await factory.WithDbAsync(db => EmailSender.ForTenantsAsync(db, [academy.Id], default));
        Assert.Equal($"Academy {academy.Code}", sender[academy.Id].FromName);
        Assert.Null(sender[academy.Id].ReplyTo);
    }
}
