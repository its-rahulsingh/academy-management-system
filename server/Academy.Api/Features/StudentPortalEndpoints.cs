using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// A student's own portal: their course, subjects and teachers, and test results. Deliberately nothing about money:
/// receipts are emailed to the student when a payment is recorded, and the agreed fee is seen only by staff. Everything is scoped to the student record
/// linked to the signed-in login, so a student only ever sees their own information. Read-only.
/// </summary>
public static class StudentPortalEndpoints
{
    public sealed record LoginRequest(string? Email, string? Password);

    public static void MapStudentPortalEndpoints(this RouteGroupBuilder academy)
    {
        // A portal login is an account, so it needs the same right as creating any other login.
        academy.MapPost("/students/{id:guid}/login", CreateLogin).RequirePermission(Permissions.UsersManage);

        var portal = academy.MapGroup("/me/student").RequirePermission(Permissions.StudentPortal);
        portal.MapGet("", Overview);
        portal.MapGet("/report-card", ReportCard);
    }

    private static async Task<Guid?> CurrentStudentIdAsync(AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == currentUser.UserId).Select(u => u.StudentId).FirstOrDefaultAsync(ct);

    private static IResult NotLinked() =>
        ApiResults.Error(StatusCodes.Status403Forbidden, "This login is not linked to a student record. Ask the academy to check it.");

    /// <summary>
    /// The student's record for the chosen academic year. A promoted student has one record per year; the login points
    /// at the newest, and earlier ones are reached through the promotion chain. No year chosen means the newest.
    /// </summary>
    private static async Task<(Guid Id, List<object> Years)?> RecordForYearAsync(
        AcademyDbContext db, ICurrentUser currentUser, Guid? yearId, CancellationToken ct)
    {
        if (await CurrentStudentIdAsync(db, currentUser, ct) is not { } current) return null;
        var ids = await PromotionEndpoints.EarlierRecordsAsync(db, current, ct);
        ids.Insert(0, current);
        var records = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.AcademicYearId, Year = s.AcademicYear.Name, s.AcademicYear.StartDate })
            .ToListAsync(ct);
        var picked = records.FirstOrDefault(r => r.AcademicYearId == yearId) ?? records.First(r => r.Id == current);
        var years = records.OrderByDescending(r => r.StartDate)
            .Select(r => (object)new { id = r.AcademicYearId, name = r.Year, selected = r.Id == picked.Id })
            .ToList();
        return (picked.Id, years);
    }

    private static async Task<IResult> Overview(Guid? yearId, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (await RecordForYearAsync(db, currentUser, yearId, ct) is not { } found) return NotLinked();
        var (id, years) = found;
        var s = await db.StudentBalances().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (s is null) return NotLinked();

        var subjectRows = await db.CourseSubjects.AsNoTracking()
            .Where(l => l.CourseId == s.CourseId && l.Subject.Active)
            .OrderBy(l => l.Subject.Name)
            .Select(l => new { l.SubjectId, l.Subject.Name })
            .ToListAsync(ct);
        var subjectIds = subjectRows.Select(r => r.SubjectId).ToList();
        // Who teaches each subject here: active faculty linked to it.
        var teachers = await db.FacultySubjects.AsNoTracking()
            .Where(l => subjectIds.Contains(l.SubjectId))
            .Join(db.Faculty.Where(f => f.Active), l => l.FacultyId, f => f.Id, (l, f) => new { l.SubjectId, f.FullName })
            .ToListAsync(ct);
        var subjects = subjectRows.Select(r => new
        {
            r.Name,
            Faculty = teachers.Where(t => t.SubjectId == r.SubjectId).Select(t => t.FullName).Order().ToList()
        });
        // Only the chosen year's tests; earlier years are a choice in the portal's year selector.
        var recentTests = await db.TestResults.AsNoTracking()
            .Where(r => r.StudentId == id)
            .OrderByDescending(r => r.Test.TestDate)
            .Take(5)
            .Select(r => new { r.Test.Title, Subject = r.Test.Subject.Name, r.Test.TestDate, r.Marks, r.Test.MaxMarks })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            years,
            student = new
            {
                s.Id, s.FullName, s.Email, s.Phone, s.CourseName, s.AcademicYearName, s.AdmissionDate, s.Active
            },
            // No fees, payments or receipts here: those go to the student by email, and the agreed fee stays with the office.
            subjects,
            recentTests = recentTests.Select(t => new
            {
                t.Title, t.Subject, t.TestDate, t.Marks, t.MaxMarks,
                percent = t.MaxMarks <= 0 ? 0 : decimal.Round(100m * t.Marks / t.MaxMarks, 1)
            })
        });
    }

    private static async Task<IResult> ReportCard(
        Guid? yearId, AcademyDbContext db, TenantAccessService access, ICurrentUser currentUser, CancellationToken ct) =>
        await RecordForYearAsync(db, currentUser, yearId, ct) is { } found
            ? await TestEndpoints.ReportCardAsync(found.Id, db, access, currentUser, ct)
            : NotLinked();

    private static async Task<IResult> CreateLogin(
        Guid id, LoginRequest request, AcademyDbContext db, SessionService sessions, ICurrentUser currentUser,
        Infrastructure.Email.EmailQueue emails, Microsoft.Extensions.Options.IOptions<Infrastructure.Email.AppOptions> appOptions, HttpContext http, CancellationToken ct)
    {
        if (!Validate.Email(request.Email)) return ApiResults.Bad("Enter the email address the student will sign in with.");
        if (!PasswordRules.IsValid(request.Password)) return ApiResults.Bad(PasswordRules.Message);

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null) return Results.NotFound();
        if (await db.Users.AnyAsync(u => u.StudentId == id, ct))
            return ApiResults.Conflict($"{student.FullName} already has a login. Reset its password from the Users screen.");

        var email = request.Email!.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return ApiResults.Conflict("That email already has an account.");

        var user = new AppUser
        {
            TenantId = currentUser.TenantId,
            Email = email,
            DisplayName = student.FullName,
            Role = Roles.Student,
            StudentId = student.Id,
            MustChangePassword = true
        };
        sessions.SetPassword(user, request.Password!);
        db.Users.Add(user);
        if (student.Email.Length == 0) student.Email = email;
        await AccountEmails.QueueWelcomeAsync(db, emails, AuthEndpoints.PublicBaseUrl(appOptions.Value, http), user, request.Password!, ct);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/users/{user.Id}", new { user.Id });
    }
}
