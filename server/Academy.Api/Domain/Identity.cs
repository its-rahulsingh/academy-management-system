namespace Academy.Api.Domain;

/// <summary>
/// A login. <see cref="TenantId"/> is null only for platform owners; every academy user belongs to one tenant.
/// Email is unique per tenant, so the same person can hold accounts at two academies.
/// </summary>
public sealed class AppUser : Entity
{
    public Guid? TenantId { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Staff";
    public bool Active { get; set; } = true;
    /// <summary>Rotated on password change or deactivation; sessions carrying an older stamp are rejected.</summary>
    public string SecurityStamp { get; set; } = NewStamp();
    public bool MustChangePassword { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    /// <summary>Set only for a faculty portal login: the faculty record this account belongs to.</summary>
    public Guid? FacultyId { get; set; }

    public static string NewStamp() => Guid.NewGuid().ToString("N");

    public void RotateStamp() => SecurityStamp = NewStamp();
}

/// <summary>A single-use password reset link. Only a hash of the token is stored.</summary>
public sealed class PasswordResetToken : Entity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}
