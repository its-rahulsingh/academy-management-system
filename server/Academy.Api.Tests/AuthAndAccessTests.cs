using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Email;
using Academy.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.Api.Tests;

[Collection(AppCollection.Name)]
public sealed class AuthAndAccessTests(AppFactory factory)
{
    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var anonymous = factory.NewBrowser();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/platform/tenants")).StatusCode);
        Assert.False((await anonymous.GetJsonAsync("/api/auth/session"))["authenticated"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task Login_needs_the_right_academy_code()
    {
        var a = await factory.NewAcademyAsync();
        var b = await factory.NewAcademyAsync();
        var email = $"admin@{a.Code}.test";
        var browser = factory.NewBrowser();

        var wrongAcademy = await browser.PostAsJsonAsync("/api/auth/login", new { academyCode = b.Code, email, password = AppFactory.Password });
        var noAcademy = await browser.PostAsJsonAsync("/api/auth/login", new { email, password = AppFactory.Password });
        var session = await browser.LoginAsync(a.Code.ToUpperInvariant(), email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Unauthorized, wrongAcademy.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, noAcademy.StatusCode);
        Assert.Equal("SuperAdmin", session["user"]!["role"]!.GetValue<string>());
        Assert.Equal(a.Code, session["tenant"]!["code"]!.GetValue<string>());
        Assert.True(session["mustChangePassword"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Account_locks_after_repeated_wrong_passwords()
    {
        var academy = await factory.NewAcademyAsync();
        var login = new { academyCode = academy.Code, email = $"admin@{academy.Code}.test", password = "definitely-wrong" };
        var browser = factory.NewBrowser();

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/api/auth/login", login)).StatusCode);

        var correct = await browser.PostAsJsonAsync("/api/auth/login", login with { password = AppFactory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
        Assert.Contains("locked", await correct.ErrorAsync());
    }

    [Fact]
    public async Task State_changing_requests_need_the_csrf_token()
    {
        var academy = await factory.NewAcademyAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/courses") { Content = JsonContent.Create(new { name = "No Token", fee = 1 }) };
        request.Headers.Add("X-XSRF-TOKEN", "forged");

        Assert.Equal(HttpStatusCode.BadRequest, (await academy.Admin.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Staff_can_run_the_front_desk_but_see_no_finance()
    {
        var academy = await factory.NewAcademyAsync();
        var studentId = await academy.NewStudentAsync(await academy.NewCourseAsync());
        var staff = await academy.NewUserAsync("Staff");
        var paymentId = (await (await academy.PayAsync(studentId, 1000, staff)).ReadJsonAsync())["id"]!.GetValue<Guid>();

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/students")).StatusCode);
        Assert.Null((await staff.GetJsonAsync("/api/dashboard"))["finance"]);
        foreach (var forbidden in new[] { "/api/expenses", "/api/payouts", "/api/faculty", "/api/reports/summary", "/api/users", "/api/audit", "/api/billing" })
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(forbidden)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "Trying" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/courses", new { name = "Not Allowed", fee = 1 })).StatusCode);
    }

    [Fact]
    public async Task Accountant_handles_money_but_not_users_or_billing()
    {
        var academy = await factory.NewAcademyAsync();
        var accountant = await academy.NewUserAsync("Accountant");

        Assert.Equal(HttpStatusCode.Created, (await accountant.PostAsJsonAsync("/api/expenses", new { title = "Chalk", amount = 50, category = "Supplies", method = "Cash" })).StatusCode);
        Assert.NotNull((await accountant.GetJsonAsync("/api/dashboard"))["finance"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync("/api/billing")).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_manage_super_admins_and_the_last_super_admin_is_protected()
    {
        var academy = await factory.NewAcademyAsync();
        var admin = await academy.NewUserAsync("Admin");
        var users = (await academy.Admin.GetJsonAsync("/api/users"))["items"]!.AsArray();
        var superAdminId = users.Single(u => u!["role"]!.GetValue<string>() == "SuperAdmin")!["id"]!.GetValue<Guid>();

        var createSuper = await admin.PostAsJsonAsync("/api/users", new { displayName = "Sneaky", email = "sneaky@x.test", role = "SuperAdmin", password = AppFactory.Password });
        var resetSuper = await admin.PutAsJsonAsync($"/api/users/{superAdminId}/password", new { password = AppFactory.Password + "!" });
        var demoteSelf = await academy.Admin.PutAsJsonAsync($"/api/users/{superAdminId}", new { displayName = "Super Admin", role = "Staff" });

        Assert.Equal(HttpStatusCode.BadRequest, createSuper.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, resetSuper.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, demoteSelf.StatusCode);
    }

    [Fact]
    public async Task Disabling_a_user_ends_their_session()
    {
        var academy = await factory.NewAcademyAsync();
        var staff = await academy.NewUserAsync("Staff");
        var staffId = (await staff.GetJsonAsync("/api/auth/session"))["user"]!["id"]!.GetValue<Guid>();

        await academy.Admin.PutAsJsonAsync($"/api/users/{staffId}/active", new { active = false });

        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/students")).StatusCode);
    }

    [Fact]
    public async Task Password_reset_link_works_once()
    {
        var academy = await factory.NewAcademyAsync();
        var email = $"admin@{academy.Code}.test";
        var browser = factory.NewBrowser();

        var forgot = await browser.PostAsJsonAsync("/api/auth/forgot-password", new { academyCode = academy.Code, email });
        var unknown = await browser.PostAsJsonAsync("/api/auth/forgot-password", new { academyCode = academy.Code, email = "nobody@nowhere.test" });
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);

        var body = await factory.WithDbAsync(db => Task.FromResult(
            db.EmailMessages.Where(m => m.ToAddress == email && m.Category == "PasswordReset").Select(m => m.Body).Single()));
        var token = Regex.Match(body, @"token=([0-9A-F]+)").Groups[1].Value;
        const string newPassword = "a-brand-new-password";

        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/auth/reset-password", new { token, password = "abcd" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsJsonAsync("/api/auth/reset-password", new { token, password = newPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/auth/reset-password", new { token, password = newPassword + "2" })).StatusCode);

        // The old password and the old session both stop working.
        Assert.Equal(HttpStatusCode.Unauthorized, (await academy.Admin.GetAsync("/api/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/api/auth/login", new { academyCode = academy.Code, email, password = AppFactory.Password })).StatusCode);
        await browser.LoginAsync(academy.Code, email, newPassword);
    }

    [Fact]
    public async Task Without_smtp_settings_queued_email_is_reported_as_not_delivered()
    {
        var academy = await factory.NewAcademyAsync();
        var email = $"admin@{academy.Code}.test";
        await factory.NewBrowser().PostAsJsonAsync("/api/auth/forgot-password", new { academyCode = academy.Code, email });

        // Other tests share the queue; keep dispatching until it is drained.
        int handled;
        do
        {
            await using var scope = factory.Services.CreateAsyncScope();
            handled = await EmailDispatchService.DispatchBatchAsync(scope.ServiceProvider, CancellationToken.None);
        } while (handled > 0);

        var message = await factory.WithDbAsync(db => Task.FromResult(
            db.EmailMessages.Single(m => m.ToAddress == email && m.Category == "PasswordReset")));
        Assert.Equal(EmailStatus.Failed, message.Status);
        Assert.Equal(EmailDispatchService.NotConfiguredError, message.LastError);

        var owner = await factory.OwnerAsync();
        var status = await owner.GetJsonAsync("/api/platform/email");
        var test = await owner.PostAsJsonAsync("/api/platform/email/test", new { to = "someone@example.test" });
        Assert.False(status["configured"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.BadRequest, test.StatusCode);
    }
}
