using System.Security.Claims;
using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Email;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Academy.Api.Features;

public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromMinutes(60);

    public sealed record LoginRequest(string? AcademyCode, string? Email, string? Password);
    public sealed record ForgotPasswordRequest(string? AcademyCode, string? Email);
    public sealed record ResetPasswordRequest(string? Token, string? Password);
    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    public sealed record SessionUser(Guid Id, string Name, string Email, string Role);
    public sealed record SessionTenant(Guid Id, string Code, string Name);
    public sealed record SessionAccess(bool Allowed, string Status, string? Reason, string? PlanName, DateOnly? PeriodEnd, bool InGrace);
    public sealed record SessionResponse(
        bool Authenticated,
        SessionUser? User,
        IReadOnlyCollection<string> Permissions,
        SessionTenant? Tenant,
        SessionAccess? Access,
        bool MustChangePassword)
    {
        public static readonly SessionResponse Anonymous = new(false, null, [], null, null, false);
    }

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapGet("/session", GetSession);
        group.MapPost("/login", Login).RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/logout", (Delegate)Logout).RequireAuthorization();
        group.MapPost("/forgot-password", ForgotPassword).RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/reset-password", ResetPassword).RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/change-password", ChangePassword).RequireAuthorization();
    }

    private static async Task<IResult> GetSession(ClaimsPrincipal principal, AcademyDbContext db, TenantAccessService access, CancellationToken ct)
    {
        if (principal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Ok(SessionResponse.Anonymous);

        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.Active, ct);
        return Results.Ok(user is null ? SessionResponse.Anonymous : await BuildSessionAsync(user, access, ct));
    }

    private static async Task<SessionResponse> BuildSessionAsync(AppUser user, TenantAccessService access, CancellationToken ct)
    {
        SessionTenant? tenant = null;
        SessionAccess? sessionAccess = null;
        if (user.TenantId is { } tenantId && await access.GetAsync(tenantId, ct) is { } state)
        {
            tenant = new SessionTenant(tenantId, state.TenantCode, state.TenantName);
            sessionAccess = new SessionAccess(state.Allowed, state.Status, state.Reason, state.PlanName, state.PeriodEnd, state.InGrace);
        }

        return new SessionResponse(true, new SessionUser(user.Id, user.DisplayName, user.Email, user.Role),
            RolePermissions.For(user.Role), tenant, sessionAccess, user.MustChangePassword);
    }

    /// <summary>Finds the account an (academy code, email) pair refers to. A blank code means a platform owner.</summary>
    private static async Task<AppUser?> FindUserAsync(AcademyDbContext db, string? academyCode, string? email, CancellationToken ct)
    {
        var normalizedEmail = (email ?? "").Trim().ToLowerInvariant();
        var code = (academyCode ?? "").Trim().ToLowerInvariant();
        if (normalizedEmail.Length == 0) return null;

        var users = db.Users.IgnoreQueryFilters().Where(u => u.Email == normalizedEmail);
        if (code.Length == 0) return await users.FirstOrDefaultAsync(u => u.TenantId == null, ct);

        var tenantId = await db.Tenants.Where(t => t.Code == code).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        return tenantId is null ? null : await users.FirstOrDefaultAsync(u => u.TenantId == tenantId, ct);
    }

    private static async Task<IResult> Login(
        LoginRequest request, AcademyDbContext db, SessionService sessions, TenantAccessService access,
        HttpContext http, CancellationToken ct)
    {
        const string invalid = "Invalid academy code, email or password.";
        var user = await FindUserAsync(db, request.AcademyCode, request.Email, ct);
        if (user is null || !user.Active)
            return ApiResults.Error(StatusCodes.Status401Unauthorized, invalid);

        if (user.LockoutEndUtc > DateTime.UtcNow)
            return ApiResults.Error(StatusCodes.Status401Unauthorized,
                "Too many failed attempts. This account is locked for a few minutes.");

        if (!sessions.VerifyPassword(user, request.Password ?? ""))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= SessionService.MaxFailedLogins)
            {
                user.LockoutEndUtc = DateTime.UtcNow.Add(SessionService.LockoutTime);
                user.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync(ct);
            return ApiResults.Error(StatusCodes.Status401Unauthorized, invalid);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            UserName = user.DisplayName,
            Action = "Login",
            EntityType = nameof(AppUser),
            EntityId = user.Id.ToString(),
            IpAddress = http.Connection.RemoteIpAddress?.ToString()
        });
        await db.SaveChangesAsync(ct);

        await sessions.SignInAsync(http, user);
        return Results.Ok(await BuildSessionAsync(user, access, ct));
    }

    private static async Task<IResult> Logout(HttpContext http)
    {
        await http.SignOutAsync();
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> ForgotPassword(
        ForgotPasswordRequest request, AcademyDbContext db, EmailQueue email, IOptions<AppOptions> appOptions,
        HttpContext http, CancellationToken ct)
    {
        var user = await FindUserAsync(db, request.AcademyCode, request.Email, ct);
        if (user is { Active: true })
        {
            var token = Tokens.NewUrlSafe();
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = Tokens.Hash(token),
                ExpiresAtUtc = DateTime.UtcNow.Add(ResetTokenLifetime)
            });
            var link = $"{PublicBaseUrl(appOptions.Value, http)}/reset-password?token={token}";
            var (subject, body) = EmailTemplates.PasswordReset(user.DisplayName, link);
            email.Enqueue(user.TenantId, user.Email, subject, body, "PasswordReset");
            await db.SaveChangesAsync(ct);
        }

        // Same answer whether or not the account exists, so this cannot be used to discover accounts.
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> ResetPassword(ResetPasswordRequest request, AcademyDbContext db, SessionService sessions, CancellationToken ct)
    {
        if (!PasswordRules.IsValid(request.Password)) return ApiResults.Bad(PasswordRules.Message);

        var hash = Tokens.Hash(request.Token ?? "");
        var token = await db.PasswordResetTokens
            .Include(t => t.User)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.UsedAtUtc is not null || token.ExpiresAtUtc < DateTime.UtcNow || !token.User.Active)
            return ApiResults.Bad("This reset link is invalid or has expired. Request a new one.");

        token.UsedAtUtc = DateTime.UtcNow;
        token.User.MustChangePassword = false;
        sessions.SetPassword(token.User, request.Password!);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest request, AcademyDbContext db, SessionService sessions, TenantAccessService access,
        ICurrentUser currentUser, HttpContext http, CancellationToken ct)
    {
        if (!PasswordRules.IsValid(request.NewPassword)) return ApiResults.Bad(PasswordRules.Message);

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == currentUser.UserId, ct);
        if (user is null) return Results.Unauthorized();
        if (!sessions.VerifyPassword(user, request.CurrentPassword ?? "")) return ApiResults.Bad("Current password is incorrect.");
        if (request.NewPassword == request.CurrentPassword) return ApiResults.Bad("Choose a password different from the current one.");

        sessions.SetPassword(user, request.NewPassword!);
        user.MustChangePassword = false;
        await db.SaveChangesAsync(ct);

        // The stamp changed, so issue a fresh cookie to keep this browser signed in; other sessions are signed out.
        await sessions.SignInAsync(http, user);
        return Results.Ok(await BuildSessionAsync(user, access, ct));
    }

    public static string PublicBaseUrl(AppOptions options, HttpContext http) =>
        string.IsNullOrWhiteSpace(options.PublicBaseUrl)
            ? $"{http.Request.Scheme}://{http.Request.Host}"
            : options.PublicBaseUrl.TrimEnd('/');
}
