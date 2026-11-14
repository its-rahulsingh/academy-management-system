using Academy.Api.Data;
using Academy.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Academy.Api.Infrastructure.Tenancy;

public sealed class SubscriptionOptions
{
    public const string Section = "Subscription";

    /// <summary>Days after the paid period ends during which the academy keeps working while it settles the invoice.</summary>
    public int GraceDays { get; set; } = 7;
    /// <summary>How many days before the period ends the renewal invoice is raised.</summary>
    public int InvoiceLeadDays { get; set; } = 7;
    public int DefaultTrialDays { get; set; } = 14;
}

/// <summary>Whether an academy may use the product right now, and the facts the UI needs to explain why not.</summary>
public sealed record TenantAccess(
    bool Allowed,
    string Status,
    string? Reason,
    string TenantName,
    string TenantCode,
    string? PlanName,
    DateOnly? PeriodEnd,
    bool InGrace,
    int? MaxStudents,
    int? MaxUsers);

public static class TenantAccessRules
{
    /// <summary>Pure decision so it can be unit-tested without a database.</summary>
    public static (bool Allowed, bool InGrace, string? Reason) Evaluate(
        TenantStatus tenantStatus, SubscriptionStatus? subscriptionStatus, DateOnly? periodEnd, DateOnly today, int graceDays)
    {
        if (tenantStatus == TenantStatus.Suspended)
            return (false, false, "This academy has been suspended. Contact the platform administrator.");
        if (subscriptionStatus is null || periodEnd is null)
            return (false, false, "This academy has no subscription yet.");
        if (subscriptionStatus == SubscriptionStatus.Cancelled)
            return (false, false, "The subscription has been cancelled.");
        if (today <= periodEnd)
            return (true, false, null);
        if (today <= periodEnd.Value.AddDays(graceDays))
            return (true, true, "Subscription payment is overdue. Access will stop when the grace period ends.");
        return (false, false, "The subscription has expired. Pay the outstanding invoice to restore access.");
    }
}

public sealed class TenantAccessService(AcademyDbContext db, IMemoryCache cache, IOptions<SubscriptionOptions> options)
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(30);

    public async Task<TenantAccess?> GetAsync(Guid tenantId, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(CacheKey(tenantId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTime;
            return await LoadAsync(tenantId, ct);
        });
    }

    public void Invalidate(Guid tenantId) => cache.Remove(CacheKey(tenantId));

    private async Task<TenantAccess?> LoadAsync(Guid tenantId, CancellationToken ct)
    {
        var row = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new
            {
                t.Name,
                t.Code,
                t.Status,
                Subscription = db.Subscriptions
                    .Where(s => s.TenantId == t.Id)
                    .Select(s => new { s.Status, s.CurrentPeriodEnd, PlanName = s.Plan.Name, s.Plan.MaxStudents, s.Plan.MaxUsers })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (allowed, inGrace, reason) = TenantAccessRules.Evaluate(
            row.Status, row.Subscription?.Status, row.Subscription?.CurrentPeriodEnd, today, options.Value.GraceDays);

        var status = row.Status == TenantStatus.Suspended ? "Suspended"
            : row.Subscription is null ? "None"
            : !allowed ? "Expired"
            : inGrace ? "PastDue"
            : row.Subscription.Status.ToString();

        return new TenantAccess(allowed, status, reason, row.Name, row.Code, row.Subscription?.PlanName,
            row.Subscription?.CurrentPeriodEnd, inGrace, row.Subscription?.MaxStudents, row.Subscription?.MaxUsers);
    }

    private static string CacheKey(Guid tenantId) => $"tenant-access:{tenantId}";
}
