using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Academy.Api.Infrastructure.Usage;
using Academy.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.Api.Tests;

[Collection(AppCollection.Name)]
public sealed class PlatformAnalyticsTests(AppFactory factory)
{
    private Task FlushUsageAsync() => factory.WithDbAsync(async db =>
    {
        await factory.Services.GetRequiredService<UsageTracker>().FlushAsync(db, CancellationToken.None);
        await UsageTracker.SnapshotDatabaseSizeAsync(db, CancellationToken.None);
        return 0;
    });

    [Theory]
    [InlineData("/api/students/{id:guid}/settle", "Settle balance")]
    [InlineData("/api/students", "Students")]
    [InlineData("/api/reports/activity/excel", "Excel downloads")]
    [InlineData("/api/reports/summary", "Reports")]
    [InlineData("/api/reports/activity", "Daily report")]
    [InlineData("/api/expenses/{id:guid}/proofs", "Expense bills")]
    [InlineData("/api/me/faculty/clock/request", "Forgot-to-clock requests")]
    [InlineData("/api/audit/dashboard", "Activity dashboard")]
    [InlineData("/api/platform/analytics/usage", "Owner console")]
    public void Requests_are_named_by_feature(string route, string feature) =>
        Assert.Equal(feature, UsageFeatures.For(route, "GET"));

    [Fact]
    public async Task Usage_is_counted_by_academy_feature_role_and_hour_without_personal_data()
    {
        var academy = await factory.NewAcademyAsync();
        var course = await academy.NewCourseAsync("Foundation", 20000);
        var student = await academy.NewStudentAsync(course, 20000, "Tara Joshi");
        await academy.PayAsync(student, 5000);
        for (var i = 0; i < 3; i++) await academy.Admin.GetAsync("/api/students?pageSize=5");
        var phone = factory.NewBrowser();
        phone.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 14) Mobile Safari/537.36");
        await phone.LoginAsync(academy.Code, $"admin@{academy.Code}.test");
        await phone.GetAsync("/api/expenses");
        await FlushUsageAsync();

        var rows = await factory.WithDbAsync(db => Task.FromResult(db.UsageHours.Where(u => u.TenantId == academy.Id).ToList()));
        Assert.True(rows.Where(r => r.Feature == "Students").Sum(r => r.Requests) >= 4);
        Assert.Contains(rows, r => r.Feature == "Fee collection" && r.Role == "SuperAdmin");
        Assert.Contains(rows, r => r.Feature == "Expenses" && r.Device == "Mobile");
        Assert.All(rows, r => Assert.Equal(r.Requests, r.Under100Ms + r.Under300Ms + r.Under1s + r.Under3s + r.Over3s));
        Assert.True(await factory.WithDbAsync(db => Task.FromResult(db.UsageUserHours.Any(u => u.TenantId == academy.Id))));

        // The owner sees it; an academy cannot reach the owner's analytics.
        var usage = await academy.Owner.GetJsonAsync("/api/platform/analytics/usage");
        var students = usage["features"]!.AsArray().Single(f => f!["feature"]!.GetValue<string>() == "Students")!;
        Assert.True(students["requests"]!.GetValue<int>() >= 4);
        Assert.True(students["academies"]!.GetValue<int>() >= 1);
        Assert.Contains("QR clock-in", usage["features"]!.AsArray().Select(f => f!["feature"]!.GetValue<string>()));
        Assert.True(usage["totals"]!["activeUsers"]!.GetValue<int>() >= 1);
        Assert.Equal(8, usage["weeks"]!.AsArray().Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await academy.Admin.GetAsync("/api/platform/analytics/usage")).StatusCode);

        var load = await academy.Owner.GetJsonAsync($"/api/platform/analytics/load?tenantId={academy.Id}");
        Assert.Equal(7, load["heatmap"]!.AsArray().Count);
        Assert.True(load["totals"]!["requests"]!.GetValue<int>() >= 6);
        Assert.True(load["totals"]!["p95Ms"]!.GetValue<int>() > 0);
        Assert.NotNull(load["peakHour"]);
        Assert.NotNull(load["quietStart"]);
        Assert.True(load["mostOnline"]!.GetValue<int>() >= 1);
        Assert.Equal(30, load["daily"]!.AsArray().Count);
        var platformLoad = await academy.Owner.GetJsonAsync("/api/platform/analytics/load");
        Assert.True(platformLoad["peakPerMinute"]!.GetValue<int>() >= 1);
        Assert.Contains(platformLoad["perAcademy"]!.AsArray(), a => a!["tenantId"]!.GetValue<Guid>() == academy.Id);
    }

    [Fact]
    public async Task Health_shows_activity_onboarding_and_totals_per_academy()
    {
        var academy = await factory.NewAcademyAsync();
        var course = await academy.NewCourseAsync("Foundation", 20000);
        var student = await academy.NewStudentAsync(course, 20000, "Kiran Das");
        await academy.PayAsync(student, 7000);
        await FlushUsageAsync();

        var health = await academy.Owner.GetJsonAsync("/api/platform/analytics/health");
        var row = health["academies"]!.AsArray().Single(a => a!["tenantId"]!.GetValue<Guid>() == academy.Id)!;
        Assert.Equal("active", row["health"]!.GetValue<string>());
        Assert.True(row["score"]!.GetValue<int>() >= 40);
        Assert.Equal(0, row["idleDays"]!.GetValue<int>());
        var steps = row["onboarding"]!.AsArray().ToDictionary(s => s!["step"]!.GetValue<string>(), s => s!["done"]!.GetValue<bool>());
        Assert.True(steps["Course added"]);
        Assert.True(steps["First fee recorded"]);
        Assert.False(steps["Faculty added"]);
        Assert.False(steps["Logo set"]);
        Assert.Equal(1, row["totals"]!["students"]!.GetValue<int>());
        Assert.Equal(7000, row["totals"]!.Money("feesTotal"));
        Assert.Contains(row["lastByRole"]!.AsArray(), r => r!["role"]!.GetValue<string>() == "SuperAdmin");
        Assert.Equal(6, health["onboardingFunnel"]!.AsArray().Count);
    }

    [Fact]
    public async Task Business_and_capacity_cover_revenue_renewals_storage_and_cost()
    {
        var academy = await factory.NewAcademyAsync();
        var business = await academy.Owner.GetJsonAsync("/api/platform/analytics/business?months=6");
        Assert.Equal(6, business["months"]!.AsArray().Count);
        Assert.True(business["academies"]!["total"]!.GetValue<int>() >= 1);
        Assert.True(business["academies"]!["trial"]!.GetValue<int>() >= 1);
        Assert.True(business["months"]!.AsArray().Last()!["newAcademies"]!.GetValue<int>() >= 1);
        Assert.NotNull(business["byPlan"]);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await academy.Owner.PutAsJsonAsync("/api/platform/analytics/settings", new { databaseLimitMb = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await academy.Owner.PutAsJsonAsync("/api/platform/analytics/settings", new { monthlyHostingCost = 2000, databaseLimitMb = 1024, emailDailyLimit = 100 })).StatusCode);
        await academy.Admin.PostAsJsonAsync("/api/expenses", new
        {
            title = "Bill", amount = 100, method = "Cash",
            proofs = new[] { new { fileName = "b.png", dataUrl = "data:image/png;base64," + Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9]) } }
        });
        await FlushUsageAsync();

        var capacity = await academy.Owner.GetJsonAsync("/api/platform/analytics/capacity");
        Assert.True(capacity["database"]!["bytes"]!.GetValue<long>() > 0);
        Assert.Equal(1024L * 1024 * 1024, capacity["database"]!["limitBytes"]!.GetValue<long>());
        Assert.NotEmpty(capacity["database"]!["tables"]!.AsArray());
        Assert.NotEmpty(capacity["database"]!["history"]!.AsArray());
        Assert.Equal(2000, capacity["settings"]!.Money("monthlyHostingCost"));
        var row = capacity["academies"]!.AsArray().Single(a => a!["tenantId"]!.GetValue<Guid>() == academy.Id)!;
        Assert.True(row["storageBytes"]!.GetValue<long>() > 0);
        Assert.Equal(10, row["filesBytes"]!.GetValue<long>());
        Assert.True(row["emails30"]!.GetValue<int>() >= 1); // the welcome email
        Assert.Equal(HttpStatusCode.Forbidden, (await academy.Admin.GetAsync("/api/platform/analytics/capacity")).StatusCode);
    }
}
