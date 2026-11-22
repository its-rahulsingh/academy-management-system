namespace Academy.Api.Domain;

public enum TenantStatus
{
    Active,
    Suspended
}

/// <summary>An academy that subscribes to the platform.</summary>
public sealed class Tenant : Entity
{
    /// <summary>Short lowercase code the academy's users type at sign-in, e.g. "brightminds".</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public string ContactName { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public DateTime? SuspendedAtUtc { get; set; }
    public string? SuspensionReason { get; set; }

    /// <summary>Short line shown under the academy name, e.g. "Excellence in coaching". Empty shows nothing.</summary>
    public string Tagline { get; set; } = "";
    /// <summary>
    /// Null when the academy has no logo. Otherwise bumped on every change and put in the logo URL,
    /// so browsers can cache the image indefinitely and still pick up a new one at once.
    /// </summary>
    public int? LogoVersion { get; set; }
    /// <summary>Whether the public admission form accepts applications.</summary>
    public bool AdmissionsOpen { get; set; } = true;

    /// <summary>Where the academy is, for faculty who clock in by location. Null until the Super Admin sets it.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    /// <summary>How far from <see cref="Latitude"/>/<see cref="Longitude"/> a location clock-in is accepted.</summary>
    public int GeofenceRadiusMeters { get; set; } = 150;
    /// <summary>Signs the rotating check-in QR codes. Created the first time a code is shown.</summary>
    public string CheckInSecret { get; set; } = "";
    /// <summary>A class running longer than this always goes for approval, and its faculty member is reminded.</summary>
    public int MaxClassMinutes { get; set; } = 180;
    /// <summary>
    /// Package faculty are not paid per class, so their QR or location clock-ins are approved without a review.
    /// Over-long classes, typed-in times and forgotten clock-in requests still wait for approval.
    /// </summary>
    public bool AutoApprovePackageClasses { get; set; } = true;

    /// <summary>An expense above this amount cannot be saved without a bill attached. Null: proof is never required.</summary>
    public decimal? ExpenseProofRequiredAbove { get; set; }
    /// <summary>Discounts and write-offs at or above this amount are red flags on the activity dashboard.</summary>
    public decimal LargeAmountAlert { get; set; } = 5000;

    /// <summary>The name the academy's emails show as their sender. Empty uses the academy name.</summary>
    public string EmailSenderName { get; set; } = "";
    /// <summary>Where replies to the academy's emails go, usually its office inbox. Empty means no reply address.</summary>
    public string ReplyToEmail { get; set; } = "";
}

/// <summary>
/// An academy's logo, kept in the database because the app's disk is not persistent on the host.
/// A table of its own so the image bytes are never read along with ordinary tenant lookups.
/// </summary>
public sealed class TenantLogo : ITenantOwned
{
    public const int MaxBytes = 200 * 1024;

    public Guid TenantId { get; set; }
    public string ContentType { get; set; } = "";
    public byte[] Data { get; set; } = [];
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum BillingInterval
{
    Monthly,
    Quarterly,
    Yearly
}

public static class BillingIntervalExtensions
{
    public static int Months(this BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly => 1,
        BillingInterval.Quarterly => 3,
        BillingInterval.Yearly => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(interval))
    };
}

/// <summary>A price list entry the platform owner sells.</summary>
public sealed class Plan : Entity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string Currency { get; set; } = "INR";
    public BillingInterval Interval { get; set; } = BillingInterval.Monthly;
    /// <summary>Null means unlimited.</summary>
    public int? MaxStudents { get; set; }
    /// <summary>Null means unlimited.</summary>
    public int? MaxUsers { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum SubscriptionStatus
{
    Trialing,
    Active,
    PastDue,
    Cancelled
}

/// <summary>
/// The single current subscription of a tenant. Price, currency and interval are copied from the plan
/// so later price-list changes do not silently re-price existing customers.
/// </summary>
public sealed class Subscription : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trialing;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "INR";
    public BillingInterval Interval { get; set; } = BillingInterval.Monthly;
    public DateOnly CurrentPeriodStart { get; set; }
    /// <summary>Last day (inclusive) the tenant has paid or trial access for.</summary>
    public DateOnly CurrentPeriodEnd { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum InvoiceStatus
{
    Open,
    Paid,
    Void
}

/// <summary>A bill from the platform to a tenant for one subscription period.</summary>
public sealed class SubscriptionInvoice : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = null!;
    public string InvoiceNumber { get; set; } = "";
    public string PlanName { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public DateOnly DueOn { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Open;
    public DateTime? PaidAtUtc { get; set; }
    /// <summary>"Razorpay" for online payments, otherwise the offline method the owner recorded.</summary>
    public string? PaymentMethod { get; set; }
    public string? ProviderOrderId { get; set; }
    public string? ProviderPaymentId { get; set; }
    public string Notes { get; set; } = "";
}
