namespace Academy.Api.Domain;

/// <summary>Append-only record of who changed what. Written automatically when the DbContext saves.</summary>
public sealed class AuditLog
{
    public long Id { get; set; }
    /// <summary>Null for platform-level actions taken by the owner.</summary>
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = "";
    /// <summary>Created, Updated, Deleted, or a named event such as Login.</summary>
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    /// <summary>JSON: new values for Created, {field: {old, new}} for Updated.</summary>
    public string? Changes { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum EmailStatus
{
    Pending,
    Sent,
    Failed
}

/// <summary>
/// Outgoing email queue. Requests only insert a row (in the same transaction as the business change);
/// a background worker delivers it with retries, so a slow or down mail server never blocks a user.
/// </summary>
public sealed class EmailMessage : Entity
{
    public Guid? TenantId { get; set; }
    public string ToAddress { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>The formatted version of the message's content (without the surrounding page), if it has one.</summary>
    public string? HtmlBody { get; set; }
    /// <summary>PasswordReset, FeeReminder, Invoice, Welcome…</summary>
    public string Category { get; set; } = "";
    public EmailStatus Status { get; set; } = EmailStatus.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
}

/// <summary>Table from the original starter project. Kept mapped so no migration drops it; not used by the app.</summary>
public sealed class Batch
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
