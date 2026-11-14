namespace Academy.Api.Domain;

/// <summary>Marks a row as belonging to exactly one academy. Enforced by a global query filter and a write guard.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

/// <summary>Base for every table. Version-7 GUIDs are time-ordered, which keeps primary-key indexes compact as tables grow.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Base for academy-owned data.</summary>
public abstract class TenantEntity : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

public enum LedgerStatus
{
    Posted,
    Voided
}

/// <summary>
/// Base for money movements. Ledger rows are never edited or deleted: a mistake is voided
/// (with who, when and why) and re-entered, so history always reconciles.
/// </summary>
public abstract class LedgerEntry : TenantEntity
{
    public LedgerStatus Status { get; set; } = LedgerStatus.Posted;
    public DateTime? VoidedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }

    public void Void(Guid? userId, string reason)
    {
        Status = LedgerStatus.Voided;
        VoidedAtUtc = DateTime.UtcNow;
        VoidedByUserId = userId;
        VoidReason = reason;
    }
}

/// <summary>Payment methods accepted across fees, payouts and expenses.</summary>
public static class PaymentMethods
{
    public static readonly IReadOnlyList<string> All = ["Cash", "Bank Transfer", "UPI", "Card", "Cheque"];

    public static bool IsValid(string? method) => method is not null && All.Contains(method);
}
