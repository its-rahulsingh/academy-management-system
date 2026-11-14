using System.Net;
using System.Net.Http.Json;
using Academy.Api.Domain;
using Academy.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Tests;

[Collection(AppCollection.Name)]
public sealed class SubscriptionTests(AppFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Moves the subscription period directly in the database, as if time had passed.</summary>
    private Task SetPeriodEndAsync(TestAcademy academy, DateOnly periodEnd) => factory.WithDbAsync(async db =>
    {
        var subscription = await db.Subscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == academy.Id);
        subscription.CurrentPeriodStart = periodEnd.AddDays(-13);
        subscription.CurrentPeriodEnd = periodEnd;
        return await db.SaveChangesAsync();
    });

    /// <summary>Access decisions are cached briefly per server; owner actions clear the cache, direct DB edits do not.</summary>
    private static Task RefreshAccessAsync(TestAcademy academy) =>
        academy.Owner.PostAsJsonAsync($"/api/platform/tenants/{academy.Id}/activate", new { });

    [Fact]
    public async Task New_academy_starts_on_a_trial_with_access()
    {
        var academy = await factory.NewAcademyAsync(trialDays: 14);

        var session = await academy.Admin.GetJsonAsync("/api/auth/session");
        var billing = await academy.Admin.GetJsonAsync("/api/billing");

        Assert.True(session["access"]!["allowed"]!.GetValue<bool>());
        Assert.Equal("Trialing", billing["subscription"]!["status"]!.GetValue<string>());
        Assert.Equal(Today.AddDays(13).ToString("yyyy-MM-dd"), billing["subscription"]!["currentPeriodEnd"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.GetAsync("/api/students")).StatusCode);
    }

    [Fact]
    public async Task Suspended_academy_is_locked_out_until_reactivated()
    {
        var academy = await factory.NewAcademyAsync();

        await academy.Owner.PostAsJsonAsync($"/api/platform/tenants/{academy.Id}/suspend", new { reason = "Chargeback" });
        var blocked = await academy.Admin.GetAsync("/api/students");
        Assert.Equal(HttpStatusCode.PaymentRequired, blocked.StatusCode);
        Assert.Equal("subscription_inactive", (await blocked.ReadJsonAsync())["code"]!.GetValue<string>());

        await academy.Owner.PostAsJsonAsync($"/api/platform/tenants/{academy.Id}/activate", new { });
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.GetAsync("/api/students")).StatusCode);
    }

    [Fact]
    public async Task Expired_academy_keeps_access_during_grace_then_loses_it_but_can_still_reach_billing()
    {
        var academy = await factory.NewAcademyAsync();

        await SetPeriodEndAsync(academy, Today.AddDays(-3));
        await RefreshAccessAsync(academy);
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.GetAsync("/api/students")).StatusCode);
        Assert.True((await academy.Admin.GetJsonAsync("/api/auth/session"))["access"]!["inGrace"]!.GetValue<bool>());

        await SetPeriodEndAsync(academy, Today.AddDays(-30));
        await RefreshAccessAsync(academy);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await academy.Admin.GetAsync("/api/students")).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await academy.Admin.PostAsJsonAsync("/api/courses", new { name = "Blocked", fee = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.GetAsync("/api/billing")).StatusCode);
        Assert.False((await academy.Admin.GetJsonAsync("/api/auth/session"))["access"]!["allowed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Paying_the_invoice_restores_access_and_extends_the_period()
    {
        var academy = await factory.NewAcademyAsync();
        await SetPeriodEndAsync(academy, Today.AddDays(-30));
        await RefreshAccessAsync(academy);

        var created = await academy.Owner.PostAsJsonAsync($"/api/platform/tenants/{academy.Id}/invoices", new { });
        var duplicate = await academy.Owner.PostAsJsonAsync($"/api/platform/tenants/{academy.Id}/invoices", new { });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var invoiceId = (await created.ReadJsonAsync())["id"]!.GetValue<Guid>();

        // Online payment is not configured in tests, so the academy is told to pay offline.
        Assert.Equal(HttpStatusCode.BadRequest, (await academy.Admin.PostAsJsonAsync($"/api/billing/invoices/{invoiceId}/checkout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await academy.Admin.PostAsJsonAsync($"/api/billing/invoices/{invoiceId}/confirm", new { orderId = "order_x", paymentId = "pay_x", signature = "bad" })).StatusCode);

        var paid = await academy.Owner.PostAsJsonAsync($"/api/platform/invoices/{invoiceId}/mark-paid", new { method = "Bank Transfer", reference = "UTR123" });
        var paidAgain = await academy.Owner.PostAsJsonAsync($"/api/platform/invoices/{invoiceId}/mark-paid", new { method = "Bank Transfer" });
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, paidAgain.StatusCode);

        var billing = await academy.Admin.GetJsonAsync("/api/billing");
        Assert.Equal("Active", billing["subscription"]!["status"]!.GetValue<string>());
        // A lapsed subscription restarts from the day it is invoiced, not from the old expiry date.
        Assert.Equal(Today.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd"), billing["subscription"]!["currentPeriodEnd"]!.GetValue<string>());
        Assert.Equal("Paid", billing["invoices"]!.AsArray().Single()!["status"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.GetAsync("/api/students")).StatusCode);
    }

    [Fact]
    public async Task Billing_cycle_raises_the_renewal_invoice_once_and_flags_lapsed_subscriptions()
    {
        var academy = await factory.NewAcademyAsync();
        await SetPeriodEndAsync(academy, Today.AddDays(-1));

        await academy.Owner.PostAsJsonAsync("/api/platform/billing/run", new { });
        await academy.Owner.PostAsJsonAsync("/api/platform/billing/run", new { });

        var (invoices, status) = await factory.WithDbAsync(async db => (
            await db.SubscriptionInvoices.IgnoreQueryFilters().CountAsync(i => i.TenantId == academy.Id),
            (await db.Subscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == academy.Id)).Status));
        Assert.Equal(1, invoices);
        Assert.Equal(SubscriptionStatus.PastDue, status);
    }

    [Fact]
    public async Task Academy_cannot_see_or_pay_another_academys_invoice()
    {
        var a = await factory.NewAcademyAsync();
        var b = await factory.NewAcademyAsync();
        var invoiceId = (await (await a.Owner.PostAsJsonAsync($"/api/platform/tenants/{a.Id}/invoices", new { })).ReadJsonAsync())["id"]!.GetValue<Guid>();

        Assert.Empty((await b.Admin.GetJsonAsync("/api/billing"))["invoices"]!.AsArray());
        Assert.Equal(HttpStatusCode.NotFound,
            (await b.Admin.PostAsJsonAsync($"/api/billing/invoices/{invoiceId}/confirm", new { orderId = "o", paymentId = "p", signature = "s" })).StatusCode);
    }

    [Fact]
    public async Task Owner_console_lists_academies_and_summarises_revenue()
    {
        var academy = await factory.NewAcademyAsync();

        var list = await academy.Owner.GetJsonAsync($"/api/platform/tenants?search={academy.Code}");
        var detail = await academy.Owner.GetJsonAsync($"/api/platform/tenants/{academy.Id}");
        var summary = await academy.Owner.GetJsonAsync("/api/platform/summary");
        var duplicate = await academy.Owner.PostAsJsonAsync("/api/platform/tenants", new
        {
            name = "Copy", code = academy.Code, planId = detail["subscription"]!["planId"]!.GetValue<Guid>(),
            superAdmin = new { name = "X", email = "x@x.test", password = AppFactory.Password }
        });

        Assert.Equal(1, list["total"]!.GetValue<int>());
        Assert.Equal("Trialing", list["items"]![0]!["subscription"]!["status"]!.GetValue<string>());
        Assert.Single(detail["users"]!.AsArray());
        Assert.True(summary["totalTenants"]!.GetValue<int>() >= 1);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Plan_limits_cap_students_and_users()
    {
        var owner = await factory.OwnerAsync();
        var code = $"lim{Guid.NewGuid():N}"[..12];
        var planId = (await (await owner.PostAsJsonAsync("/api/platform/plans",
            new { code, name = "Tiny", price = 100, interval = "Monthly", maxStudents = 1, maxUsers = 1 })).ReadJsonAsync())["id"]!.GetValue<Guid>();
        var academy = await factory.NewAcademyAsync();
        await owner.PutAsJsonAsync($"/api/platform/tenants/{academy.Id}/subscription",
            new { planId, currentPeriodEnd = Today.AddDays(30), status = "Active" });

        var courseId = await academy.NewCourseAsync();
        await academy.NewStudentAsync(courseId);
        var secondStudent = await academy.Admin.PostAsJsonAsync("/api/students", new { fullName = "One Too Many", courseId, feeAmount = 1 });
        var secondUser = await academy.Admin.PostAsJsonAsync("/api/users", new { displayName = "Extra", email = "extra@x.test", role = "Staff", password = AppFactory.Password });

        Assert.Equal(HttpStatusCode.BadRequest, secondStudent.StatusCode);
        Assert.Contains("plan allows 1", await secondStudent.ErrorAsync());
        Assert.Equal(HttpStatusCode.BadRequest, secondUser.StatusCode);
    }
}
