using System.Security.Claims;

namespace Academy.Api.Infrastructure.Tenancy;

/// <summary>Who is making the current request, and for which academy. All tenant isolation derives from this.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    /// <summary>Null for platform owners, anonymous requests and background jobs.</summary>
    Guid? TenantId { get; }
    string UserName { get; }
    string? Role { get; }
    string? IpAddress { get; }
}

public static class AppClaims
{
    public const string TenantId = "tenant_id";
    public const string SecurityStamp = "security_stamp";
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => ParseGuid(Principal?.FindFirstValue(ClaimTypes.NameIdentifier));
    public Guid? TenantId => ParseGuid(Principal?.FindFirstValue(AppClaims.TenantId));
    public string UserName => Principal?.Identity?.Name ?? "system";
    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}

/// <summary>Used by design-time tooling and background jobs, where there is no signed-in user.</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public Guid? TenantId => null;
    public string UserName => "system";
    public string? Role => null;
    public string? IpAddress => null;
}
