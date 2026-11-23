using Academy.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Infrastructure.Tenancy;

/// <summary>
/// Plan limits that are checked where something is added. Limits never remove anything: an academy above a limit
/// (for example after moving to the Free plan) keeps all its records and simply cannot add more.
/// </summary>
public static class PlanLimits
{
    public const string ReceiptCategory = "Receipt";

    /// <summary>Whether the academy may email another fee receipt this calendar month (India time).</summary>
    public static async Task<bool> ReceiptEmailAllowedAsync(AcademyDbContext db, TenantAccessService access, Guid? tenantId, CancellationToken ct)
    {
        if (tenantId is not { } id) return true;
        if ((await access.GetAsync(id, ct))?.MaxReceiptEmailsPerMonth is not { } max) return true;
        var sent = await db.EmailMessages.CountAsync(m => m.TenantId == id && m.Category == ReceiptCategory && m.CreatedAtUtc >= MonthStartUtc(), ct);
        return sent < max;
    }

    /// <summary>An error to show when adding one more active faculty member would pass the plan's limit, otherwise null.</summary>
    public static async Task<string?> FacultyLimitAsync(AcademyDbContext db, TenantAccessService access, Guid tenantId, CancellationToken ct)
    {
        if ((await access.GetAsync(tenantId, ct))?.MaxFaculty is not { } max) return null;
        return await db.Faculty.CountAsync(f => f.Active, ct) >= max
            ? $"Your plan allows {max} active faculty members. Mark one inactive or upgrade the plan to add more."
            : null;
    }

    /// <summary>The start of the current month in India, as a UTC instant.</summary>
    public static DateTime MonthStartUtc()
    {
        var india = TimeSpan.FromHours(5.5);
        var now = DateTime.UtcNow + india;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc) - india;
    }
}
