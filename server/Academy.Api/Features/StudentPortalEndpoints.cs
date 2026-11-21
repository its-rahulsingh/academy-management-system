using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// A student's own portal: their course, fees, receipts and test results. Everything is scoped to the student record
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
        portal.MapGet("/receipts/{paymentId:guid}", Receipt);
        portal.MapGet("/report-card", ReportCard);
    }

    private static async Task<Guid?> CurrentStudentIdAsync(AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == currentUser.UserId).Select(u => u.StudentId).FirstOrDefaultAsync(ct);

    private static IResult NotLinked() =>
        ApiResults.Error(StatusCodes.Status403Forbidden, "This login is not linked to a student record. Ask the academy to check it.");

    private static async Task<IResult> Overview(AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (await CurrentStudentIdAsync(db, currentUser, ct) is not { } id) return NotLinked();
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
        // Voided entries were mistakes; the student sees only what stands.
        var payments = await db.StudentPayments.AsNoTracking()
            .Where(p => p.StudentId == id && p.Status == LedgerStatus.Posted)
            .OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.ReceiptNumber)
            .Select(p => new { p.Id, p.Kind, p.ReceiptNumber, p.Amount, p.Method, p.PaidOn })
            .ToListAsync(ct);
        var adjustments = await db.FeeAdjustments.AsNoTracking()
            .Where(a => a.StudentId == id && a.Status == LedgerStatus.Posted)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new { a.Kind, a.Amount, a.Reason, a.CreatedAtUtc })
            .ToListAsync(ct);
        var recentTests = await db.TestResults.AsNoTracking()
            .Where(r => r.StudentId == id)
            .OrderByDescending(r => r.Test.TestDate)
            .Take(5)
            .Select(r => new { r.Test.Title, Subject = r.Test.Subject.Name, r.Test.TestDate, r.Marks, r.Test.MaxMarks })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            student = new
            {
                s.Id, s.FullName, s.Email, s.Phone, s.CourseName, s.AcademicYearName, s.AdmissionDate, s.Active
            },
            fees = new { s.FeeAmount, s.Discounts, s.Surcharges, s.Payable, s.Paid, s.Pending, s.Status },
            subjects,
            payments = payments.Select(p => new { p.Id, p.Kind, receipt = StudentLedger.ReceiptLabel(p.ReceiptNumber), p.Amount, p.Method, p.PaidOn }),
            adjustments,
            recentTests = recentTests.Select(t => new
            {
                t.Title, t.Subject, t.TestDate, t.Marks, t.MaxMarks,
                percent = t.MaxMarks <= 0 ? 0 : decimal.Round(100m * t.Marks / t.MaxMarks, 1)
            })
        });
    }

    private static async Task<IResult> Receipt(
        Guid paymentId, AcademyDbContext db, TenantAccessService access, ICurrentUser currentUser, CancellationToken ct) =>
        await CurrentStudentIdAsync(db, currentUser, ct) is { } id
            ? await PaymentEndpoints.ReceiptAsync(paymentId, id, db, access, currentUser, ct)
            : NotLinked();

    private static async Task<IResult> ReportCard(AcademyDbContext db, TenantAccessService access, ICurrentUser currentUser, CancellationToken ct) =>
        await CurrentStudentIdAsync(db, currentUser, ct) is { } id
            ? await TestEndpoints.ReportCardAsync(id, db, access, currentUser, ct)
            : NotLinked();

    private static async Task<IResult> CreateLogin(
        Guid id, LoginRequest request, AcademyDbContext db, SessionService sessions, ICurrentUser currentUser, CancellationToken ct)
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
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/users/{user.Id}", new { user.Id });
    }
}
