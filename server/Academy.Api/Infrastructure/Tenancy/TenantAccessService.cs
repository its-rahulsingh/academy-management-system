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
    /// <summary>Trial given when the owner does not choose one. 0: academies start on their plan straight away.</summary>
    public int DefaultTrialDays { get; set; } = 0;
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
    int? MaxUsers,
    string Tagline,
    int? LogoVersion,
    int? MaxFaculty = null,
    int? MaxReceiptEmailsPerMonth = null,
    bool CustomBranding = true,
    bool Free = false);

public static class TenantAccessRules
{
    /// <summary>Pure decision so it can be unit-tested without a database.</summary>
    /// <param name="free">A free subscription (price 0) never expires; only suspension or cancellation stops it.</param>
    public static (bool Allowed, bool InGrace, string? Reason) Evaluate(
        TenantStatus tenantStatus, SubscriptionStatus? subscriptionStatus, DateOnly? periodEnd, DateOnly today, int graceDays, bool free = false)
    {
        if (tenantStatus == TenantStatus.Suspended)
            return (false, false, "This academy has been suspended. Contact the platform administrator.");
        if (subscriptionStatus is null || periodEnd is null)
            return (false, false, "This academy has no subscription yet.");
        if (subscriptionStatus == SubscriptionStatus.Cancelled)
            return (false, false, "The subscription has been cancelled.");
        if (free)
            return (true, false, null);
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
                t.Tagline,
                t.LogoVersion,
                Subscription = db.Subscriptions
                    .Where(s => s.TenantId == t.Id)
                    .Select(s => new
                    {
                        s.Status, s.CurrentPeriodEnd, s.Price, PlanName = s.Plan.Name, s.Plan.MaxStudents, s.Plan.MaxUsers,
                        s.Plan.MaxFaculty, s.Plan.MaxReceiptEmailsPerMonth, s.Plan.CustomBranding
                    })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (allowed, inGrace, reason) = TenantAccessRules.Evaluate(
            row.Status, row.Subscription?.Status, row.Subscription?.CurrentPeriodEnd, today, options.Value.GraceDays,
            free: row.Subscription is { Price: <= 0 });

        var status = row.Status == TenantStatus.Suspended ? "Suspended"
            : row.Subscription is null ? "None"
            : !allowed ? "Expired"
            : inGrace ? "PastDue"
            : row.Subscription.Status.ToString();

        // On a plan without custom branding the logo is hidden everywhere it would be shown; the stored file is kept.
        var branding = row.Subscription?.CustomBranding ?? true;
        return new TenantAccess(allowed, status, reason, row.Name, row.Code, row.Subscription?.PlanName,
            row.Subscription?.CurrentPeriodEnd, inGrace, row.Subscription?.MaxStudents, row.Subscription?.MaxUsers,
            row.Tagline, branding ? row.LogoVersion : null,
            row.Subscription?.MaxFaculty, row.Subscription?.MaxReceiptEmailsPerMonth, branding, row.Subscription is { Price: <= 0 });
    }

    private static string CacheKey(Guid tenantId) => $"tenant-access:{tenantId}";
}
