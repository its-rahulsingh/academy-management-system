using System.Collections.Frozen;
using System.Security.Claims;

namespace Academy.Api.Infrastructure.Auth;

public static class Roles
{
    /// <summary>Runs the platform: creates academies and manages subscriptions. Has no academy of its own.</summary>
    public const string Owner = "Owner";
    /// <summary>The academy's top account, issued by the owner. Manages every user and the subscription.</summary>
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Accountant = "Accountant";
    public const string Staff = "Staff";
    /// <summary>
    /// A faculty member's own portal login: logs teaching time and sees their own pay, nothing else.
    /// Created from the faculty record (never picked from the Users screen), so it is always linked to one.
    /// </summary>
    public const string Faculty = "Faculty";

    /// <summary>Staff roles an academy can hold, most privileged first. These can be picked on the Users screen.</summary>
    public static readonly IReadOnlyList<string> Tenant = [SuperAdmin, Admin, Accountant, Staff];

    /// <summary>Roles the given role may pick when creating a user or changing a user's role.</summary>
    public static IReadOnlyList<string> AssignableBy(string? role) => role switch
    {
        Owner or SuperAdmin => Tenant,
        Admin => [Admin, Accountant, Staff],
        _ => []
    };

    /// <summary>Roles whose accounts the given role may manage (rename, reset password, deactivate).</summary>
    public static IReadOnlyList<string> ManageableBy(string? role) => role switch
    {
        Owner or SuperAdmin or Admin => [.. AssignableBy(role), Faculty],
        _ => []
    };
}

/// <summary>
/// Endpoints demand a permission, never a role, so a new role (or a per-academy custom role later)
/// is one entry in <see cref="RolePermissions"/> rather than a sweep through every endpoint.
/// </summary>
public static class Permissions
{
    public const string DashboardFinance = "dashboard.finance";
    public const string StudentsRead = "students.read";
    public const string StudentsWrite = "students.write";
    public const string CoursesRead = "courses.read";
    public const string CoursesWrite = "courses.write";
    public const string FacultyRead = "faculty.read";
    public const string FacultyWrite = "faculty.write";
    public const string PaymentsRead = "payments.read";
    public const string PaymentsWrite = "payments.write";
    /// <summary>Void entries, issue refunds, grant discounts.</summary>
    public const string LedgerCorrect = "ledger.correct";
    public const string PayoutsRead = "payouts.read";
    public const string PayoutsWrite = "payouts.write";
    public const string ExpensesRead = "expenses.read";
    public const string ExpensesWrite = "expenses.write";
    public const string TestsRead = "tests.read";
    public const string TestsWrite = "tests.write";
    public const string ReportsRead = "reports.read";
    public const string RemindersSend = "reminders.send";
    public const string UsersManage = "users.manage";
    public const string AuditRead = "audit.read";
    public const string BillingManage = "billing.manage";
    /// <summary>Add academic years and change the dates of the current and future ones.</summary>
    public const string YearsManage = "years.manage";
    /// <summary>Change how the academy presents itself: logo and tagline.</summary>
    public const string BrandingManage = "branding.manage";
    /// <summary>Review applications from the public admission form and open or close the form.</summary>
    public const string AdmissionsManage = "admissions.manage";
    /// <summary>The staff workspace (dashboard and year switch). Every role has it except a faculty portal login.</summary>
    public const string Workspace = "workspace.use";
    /// <summary>A faculty member's own portal: their sessions and pay.</summary>
    public const string FacultyPortal = "faculty.portal";
    /// <summary>The day-by-day activity report of everything that happened in the academy.</summary>
    public const string ReportsActivity = "reports.activity";

    public static readonly IReadOnlyList<string> All =
    [
        DashboardFinance, StudentsRead, StudentsWrite, CoursesRead, CoursesWrite, FacultyRead, FacultyWrite,
        PaymentsRead, PaymentsWrite, LedgerCorrect, PayoutsRead, PayoutsWrite, ExpensesRead, ExpensesWrite,
        TestsRead, TestsWrite, ReportsRead, RemindersSend, UsersManage, AuditRead, BillingManage, YearsManage,
        BrandingManage, AdmissionsManage, Workspace, FacultyPortal, ReportsActivity
    ];
}

public static class RolePermissions
{
    private static readonly FrozenDictionary<string, FrozenSet<string>> Map = new Dictionary<string, FrozenSet<string>>
    {
        [Roles.SuperAdmin] = Permissions.All.Except([Permissions.FacultyPortal]).ToFrozenSet(),
        [Roles.Admin] = Permissions.All.Except(
            [Permissions.BillingManage, Permissions.YearsManage, Permissions.BrandingManage, Permissions.ReportsActivity, Permissions.FacultyPortal]).ToFrozenSet(),
        [Roles.Accountant] = new[]
        {
            Permissions.Workspace, Permissions.DashboardFinance, Permissions.StudentsRead, Permissions.CoursesRead, Permissions.FacultyRead,
            Permissions.PaymentsRead, Permissions.PaymentsWrite, Permissions.LedgerCorrect, Permissions.PayoutsRead,
            Permissions.PayoutsWrite, Permissions.ExpensesRead, Permissions.ExpensesWrite, Permissions.ReportsRead,
            Permissions.RemindersSend
        }.ToFrozenSet(),
        // Front desk: admits students, collects fees, records marks. Sees no expenses, payouts or totals.
        [Roles.Staff] = new[]
        {
            Permissions.Workspace, Permissions.StudentsRead, Permissions.StudentsWrite, Permissions.CoursesRead, Permissions.PaymentsRead,
            Permissions.PaymentsWrite, Permissions.TestsRead, Permissions.TestsWrite, Permissions.AdmissionsManage
        }.ToFrozenSet(),
        [Roles.Faculty] = new[] { Permissions.FacultyPortal }.ToFrozenSet()
    }.ToFrozenDictionary();

    public static IReadOnlyCollection<string> For(string? role) =>
        role is not null && Map.TryGetValue(role, out var set) ? set : [];

    public static bool Has(string? role, string permission) =>
        role is not null && Map.TryGetValue(role, out var set) && set.Contains(permission);

    public static bool Has(this ClaimsPrincipal user, string permission) =>
        Has(user.FindFirstValue(ClaimTypes.Role), permission);
}

public static class Policies
{
    public const string Owner = "platform.owner";
}

public static class AuthorizationExtensions
{
    public static IServiceCollection AddAcademyAuthorization(this IServiceCollection services) =>
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.Owner, policy => policy.RequireRole(Roles.Owner));
            foreach (var permission in Permissions.All)
                options.AddPolicy(permission, policy => policy.RequireAssertion(ctx => ctx.User.Has(permission)));
        });

    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder => builder.RequireAuthorization(permission);
}
