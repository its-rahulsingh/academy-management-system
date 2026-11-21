using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Academy.Api.Infrastructure.Auth;

public static class PasswordRules
{
    public const int MinimumLength = 5;
    public const string Message = "Password must contain at least 5 characters.";

    public static bool IsValid(string? password) => password is { Length: >= MinimumLength and <= 200 };
}

public static class Tokens
{
    public static string NewUrlSafe() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>Creates sign-in cookies and re-validates them against the database on later requests.</summary>
public sealed class SessionService(IMemoryCache cache, IPasswordHasher<AppUser> hasher)
{
    /// <summary>
    /// How long a disabled account or changed password can keep working on a server that has not yet re-read the user.
    /// Trades one query per request for one query per user per window.
    /// </summary>
    private static readonly TimeSpan StampCacheTime = TimeSpan.FromSeconds(30);

    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(15);

    public void SetPassword(AppUser user, string password)
    {
        user.PasswordHash = hasher.HashPassword(user, password);
        user.RotateStamp();
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        Forget(user.Id);
    }

    public bool VerifyPassword(AppUser user, string password) =>
        hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;

    public Task SignInAsync(HttpContext http, AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new(AppClaims.SecurityStamp, user.SecurityStamp)
        };
        if (user.TenantId is { } tenantId) claims.Add(new Claim(AppClaims.TenantId, tenantId.ToString()));
        if (user.MustChangePassword) claims.Add(new Claim(AppClaims.MustChangePassword, "true"));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    /// <summary>Call after disabling a user or changing a password so this server rejects old cookies immediately.</summary>
    public void Forget(Guid userId) => cache.Remove(CacheKey(userId));

    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.RejectPrincipal();
            return;
        }

        var stamp = await cache.GetOrCreateAsync(CacheKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = StampCacheTime;
            var db = context.HttpContext.RequestServices.GetRequiredService<AcademyDbContext>();
            return await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == userId && u.Active)
                .Select(u => u.SecurityStamp)
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
        });

        if (stamp is null || stamp != principal!.FindFirstValue(AppClaims.SecurityStamp))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
        }
    }

    private static string CacheKey(Guid userId) => $"session-stamp:{userId}";
}
