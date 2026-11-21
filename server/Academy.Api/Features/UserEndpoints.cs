using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>An academy managing its own logins. The tenant filter already limits every query to that academy.</summary>
public static class UserEndpoints
{
    public sealed record CreateUserRequest(string? DisplayName, string? Email, string? Role, string? Password);
    public sealed record UpdateUserRequest(string? DisplayName, string? Role);
    public sealed record SetPasswordRequest(string? Password);
    public sealed record SetActiveRequest(bool Active);

    public static void MapUserEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/users").RequirePermission(Permissions.UsersManage);
        group.MapGet("", List);
        group.MapPost("", Create);
        group.MapPut("/{id:guid}", Update);
        group.MapPut("/{id:guid}/password", SetPassword);
        group.MapPut("/{id:guid}/active", SetActive);
    }

    private static async Task<IResult> List(AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var manageable = Roles.ManageableBy(currentUser.Role);
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.Email, u.DisplayName, u.Role, u.Active, u.LastLoginAtUtc })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            assignableRoles = Roles.AssignableBy(currentUser.Role),
            items = users.Select(u => new
            {
                u.Id, u.Email, u.DisplayName, u.Role, u.Active, u.LastLoginAtUtc,
                canManage = manageable.Contains(u.Role)
            })
        });
    }

    private static async Task<IResult> Create(
        CreateUserRequest request, AcademyDbContext db, SessionService sessions, TenantAccessService access,
        ICurrentUser currentUser, CancellationToken ct)
    {
        if (!Validate.Name(request.DisplayName, 140) || !Validate.Email(request.Email))
            return ApiResults.Bad("Enter a name and a valid email address.");
        if (!PasswordRules.IsValid(request.Password)) return ApiResults.Bad(PasswordRules.Message);
        if (!Roles.AssignableBy(currentUser.Role).Contains(request.Role ?? ""))
            return ApiResults.Bad(request.Role switch
            {
                Roles.Faculty => "Give a faculty member a login from the Faculty screen, so it is linked to their record.",
                Roles.Student => "Give a student a login from their student record, so it is linked to it.",
                _ => "You cannot create an account with that role."
            });

        var email = request.Email!.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return ApiResults.Conflict("That email already has an account.");

        if (await PlanUserLimitAsync(db, access, currentUser, ct) is { } limit) return limit;

        var user = new AppUser
        {
            TenantId = currentUser.TenantId,
            Email = email,
            DisplayName = request.DisplayName!.Trim(),
            Role = request.Role!,
            MustChangePassword = true
        };
        sessions.SetPassword(user, request.Password!);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/users/{user.Id}", new { user.Id });
    }

    private static async Task<IResult> Update(Guid id, UpdateUserRequest request, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var (user, error) = await FindManageableAsync(db, currentUser, id, ct);
        if (user is null) return error!;
        if (!Validate.Name(request.DisplayName, 140)) return ApiResults.Bad("Enter a name.");
        // A faculty login stays a faculty login: its role is tied to the faculty record it belongs to.
        if (IsPortalRole(user.Role) || IsPortalRole(request.Role)) {
            if (user.Role != request.Role)
                return ApiResults.Bad("A faculty or student login cannot be changed to another role, or a staff login to one of those.");
        }
        else if (!Roles.AssignableBy(currentUser.Role).Contains(request.Role ?? ""))
            return ApiResults.Bad("You cannot assign that role.");
        if (user.Id == currentUser.UserId && user.Role != request.Role)
            return ApiResults.Bad("You cannot change your own role.");
        if (user.Role == Roles.SuperAdmin && request.Role != Roles.SuperAdmin && await IsLastSuperAdminAsync(db, user.Id, ct))
            return ApiResults.Bad("An academy must keep at least one active Super Admin.");

        user.DisplayName = request.DisplayName!.Trim();
        if (user.Role != request.Role)
        {
            user.Role = request.Role!;
            // The role lives in the session cookie, so force the user to sign in again.
            user.RotateStamp();
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { user.Id });
    }

    private static async Task<IResult> SetPassword(
        Guid id, SetPasswordRequest request, AcademyDbContext db, SessionService sessions, ICurrentUser currentUser, CancellationToken ct)
    {
        var (user, error) = await FindManageableAsync(db, currentUser, id, ct);
        if (user is null) return error!;
        if (!PasswordRules.IsValid(request.Password)) return ApiResults.Bad(PasswordRules.Message);

        sessions.SetPassword(user, request.Password!);
        user.MustChangePassword = user.Id != currentUser.UserId;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> SetActive(
        Guid id, SetActiveRequest request, AcademyDbContext db, SessionService sessions, ICurrentUser currentUser, CancellationToken ct)
    {
        var (user, error) = await FindManageableAsync(db, currentUser, id, ct);
        if (user is null) return error!;
        if (!request.Active && user.Id == currentUser.UserId) return ApiResults.Bad("You cannot deactivate your own account.");
        if (!request.Active && user.Role == Roles.SuperAdmin && await IsLastSuperAdminAsync(db, user.Id, ct))
            return ApiResults.Bad("An academy must keep at least one active Super Admin.");

        user.Active = request.Active;
        user.RotateStamp();
        sessions.Forget(user.Id);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    /// <summary>
    /// The plan's user limit counts staff logins. Faculty portal logins are left out: an academy may have many
    /// hourly tutors, and their login only reaches their own pay.
    /// </summary>
    internal static async Task<IResult?> PlanUserLimitAsync(AcademyDbContext db, TenantAccessService access, ICurrentUser currentUser, CancellationToken ct)
    {
        var limits = await access.GetAsync(currentUser.TenantId!.Value, ct);
        return limits?.MaxUsers is { } max && await db.Users.CountAsync(u => u.Active && u.Role != Roles.Faculty && u.Role != Roles.Student, ct) >= max
            ? ApiResults.Bad($"Your plan allows {max} active users. Deactivate one or upgrade the plan.")
            : null;
    }

    /// <summary>Faculty and student logins belong to a record; their role never changes.</summary>
    private static bool IsPortalRole(string? role) => role is Roles.Faculty or Roles.Student;

    private static async Task<(AppUser? User, IResult? Error)> FindManageableAsync(
        AcademyDbContext db, ICurrentUser currentUser, Guid id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return (null, Results.NotFound());
        return Roles.ManageableBy(currentUser.Role).Contains(user.Role)
            ? (user, null)
            : (null, ApiResults.Error(StatusCodes.Status403Forbidden, "You cannot manage an account with that role."));
    }

    private static async Task<bool> IsLastSuperAdminAsync(AcademyDbContext db, Guid userId, CancellationToken ct) =>
        !await db.Users.AnyAsync(u => u.Id != userId && u.Role == Roles.SuperAdmin && u.Active, ct);
}
