using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// Moving continuing students into the next academic year. Each promoted student gets a new record in the new year
/// (new course and fee), linked to last year's, and their portal login moves with them. Last year's record keeps its
/// own payments and any unpaid balance, which stays collectable and is shown on the new record as "previous year due".
/// </summary>
public static class PromotionEndpoints
{
    public sealed record PromoteItem(Guid StudentId, Guid CourseId, decimal FeeAmount);
    public sealed record PromoteRequest(Guid FromYearId, Guid ToYearId, IReadOnlyList<PromoteItem>? Students, Guid? CourseId, bool MarkOthersInactive);

    private const int MaxPerRequest = 500;

    public static void MapPromotionEndpoints(this RouteGroupBuilder academy)
    {
        academy.MapGet("/students/promotion", Candidates).RequirePermission(Permissions.StudentsWrite);
        academy.MapPost("/students/promote", Promote).RequirePermission(Permissions.StudentsWrite);
    }

    private static async Task<List<AcademicYear>> YearsAsync(AcademyDbContext db, CancellationToken ct) =>
        await db.AcademicYears.AsNoTracking().OrderBy(y => y.StartDate).ToListAsync(ct);

    /// <summary>Students of one year (optionally one course) and whether each has already been promoted.</summary>
    private static async Task<IResult> Candidates(Guid? fromYearId, Guid? courseId, AcademyDbContext db, CancellationToken ct)
    {
        var years = await YearsAsync(db, ct);
        var today = Validate.Today;
        // By default, from the current year (or the last one that has started) into the one after it.
        var from = years.FirstOrDefault(y => y.Id == fromYearId)
                   ?? years.LastOrDefault(y => y.StartDate <= today)
                   ?? years.FirstOrDefault();
        if (from is null) return ApiResults.Bad("Set up an academic year first.");
        var to = years.FirstOrDefault(y => y.StartDate > from.EndDate);

        var students = await db.StudentBalances()
            .Where(s => s.AcademicYearId == from.Id && (courseId == null || s.CourseId == courseId))
            .OrderBy(s => s.CourseName).ThenBy(s => s.FullName)
            .Select(s => new
            {
                s.Id, s.FullName, s.CourseId, s.CourseName, s.FeeAmount, s.Active,
                Due = s.FeeAmount - s.Discounts + s.Surcharges - (s.Received - s.Refunded) - s.WrittenOff,
                PromotedTo = db.Students.Where(n => n.PreviousStudentId == s.Id).Select(n => n.AcademicYear.Name).FirstOrDefault()
            })
            .ToListAsync(ct);
        var courses = await db.Courses.AsNoTracking().Where(c => c.Active).OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.Fee }).ToListAsync(ct);

        return Results.Ok(new
        {
            years = years.Select(y => new { y.Id, y.Name, y.StartDate, y.EndDate, isClosed = y.IsClosedOn(today) }),
            from = new { from.Id, from.Name },
            to = to is null ? null : new { to.Id, to.Name, to.StartDate },
            courses,
            students = students.Select(s => new
            {
                s.Id, s.FullName, s.CourseId, s.CourseName, s.FeeAmount, s.Active, due = Math.Max(0, s.Due), s.PromotedTo
            })
        });
    }

    private static async Task<IResult> Promote(
        PromoteRequest request, AcademyDbContext db, TenantAccessService access, ICurrentUser currentUser, CancellationToken ct)
    {
        var items = request.Students ?? [];
        if (items.Count == 0 && !request.MarkOthersInactive) return ApiResults.Bad("Tick the students who are continuing.");
        if (items.Count > MaxPerRequest) return ApiResults.Bad($"Promote at most {MaxPerRequest} students at a time.");
        if (items.Select(i => i.StudentId).Distinct().Count() != items.Count) return ApiResults.Bad("A student is listed twice.");
        if (items.Any(i => !Validate.AmountOrZero(i.FeeAmount))) return ApiResults.Bad("Enter a valid fee for every student.");

        var years = await YearsAsync(db, ct);
        var from = years.FirstOrDefault(y => y.Id == request.FromYearId);
        var to = years.FirstOrDefault(y => y.Id == request.ToYearId);
        if (from is null || to is null) return ApiResults.Bad("Choose the year to promote from and the year to promote into.");
        if (to.StartDate <= from.EndDate) return ApiResults.Bad($"{to.Name} must start after {from.Name} ends.");
        var today = Validate.Today;
        if (to.IsClosedOn(today)) return ApiResults.Bad($"{to.Name} has already ended.");

        var courseIds = items.Select(i => i.CourseId).Distinct().ToList();
        var activeCourses = await db.Courses.Where(c => courseIds.Contains(c.Id) && c.Active).Select(c => c.Id).ToListAsync(ct);
        if (activeCourses.Count != courseIds.Count) return ApiResults.Bad("Choose an active course for every student.");

        // The plan's student limit counts active students; promoting adds a new active record and may retire the old one.
        var limits = await access.GetAsync(currentUser.TenantId!.Value, ct);

        return await db.InTransactionAsync<IResult>(async () =>
        {
            var ids = items.Select(i => i.StudentId).ToList();
            var students = await db.Students.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
            if (students.Count != ids.Count) return ApiResults.Bad("A student could not be found.");
            if (students.Any(s => s.AcademicYearId != from.Id)) return ApiResults.Bad($"Every student must be from {from.Name}.");
            var already = await db.Students.Where(n => n.PreviousStudentId != null && ids.Contains(n.PreviousStudentId.Value))
                .Join(db.Students, n => n.PreviousStudentId, o => o.Id, (_, o) => o.FullName).ToListAsync(ct);
            if (already.Count > 0) return ApiResults.Bad($"Already promoted: {string.Join(", ", already.Take(5))}.");

            if (limits?.MaxStudents is { } max)
            {
                var active = await db.Students.CountAsync(s => s.Active, ct);
                var retiring = students.Count(s => s.Active);
                if (active - retiring + students.Count > max)
                    return ApiResults.Bad($"Your plan allows {max} active students. Upgrade the plan to promote this many.");
            }

            var admission = today > to.StartDate ? today : to.StartDate;
            var created = new Dictionary<Guid, Guid>();
            foreach (var item in items)
            {
                var old = students.First(s => s.Id == item.StudentId);
                var next = new Student
                {
                    AcademicYearId = to.Id,
                    FullName = old.FullName,
                    Email = old.Email,
                    Phone = old.Phone,
                    CourseId = item.CourseId,
                    FeeAmount = item.FeeAmount,
                    AdmissionDate = admission,
                    Remarks = old.Remarks,
                    PreviousStudentId = old.Id
                };
                db.Students.Add(next);
                // Last year's record stays for its payments and dues; the student carries on with the new one.
                old.Active = false;
                created[old.Id] = next.Id;
            }

            // A student's portal login moves to their new record, so they see the new year straight away.
            var logins = await db.Users.Where(u => u.StudentId != null && ids.Contains(u.StudentId.Value)).ToListAsync(ct);
            foreach (var login in logins) login.StudentId = created[login.StudentId!.Value];

            var deactivated = 0;
            if (request.MarkOthersInactive)
            {
                var others = await db.Students
                    .Where(s => s.AcademicYearId == from.Id && s.Active && !ids.Contains(s.Id)
                                && (request.CourseId == null || s.CourseId == request.CourseId)
                                && !db.Students.Any(n => n.PreviousStudentId == s.Id))
                    .ToListAsync(ct);
                foreach (var s in others) s.Active = false;
                deactivated = others.Count;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { promoted = created.Count, deactivated, toYear = to.Name });
        }, ct);
    }

    /// <summary>The student's earlier records, newest first (the chain of promotions behind this record).</summary>
    public static async Task<List<Guid>> EarlierRecordsAsync(AcademyDbContext db, Guid studentId, CancellationToken ct)
    {
        var chain = new List<Guid>();
        var current = await db.Students.Where(s => s.Id == studentId).Select(s => s.PreviousStudentId).FirstOrDefaultAsync(ct);
        // Bounded: a student is not promoted more than a handful of times.
        while (current is { } previous && chain.Count < 15 && !chain.Contains(previous))
        {
            chain.Add(previous);
            current = await db.Students.Where(s => s.Id == previous).Select(s => s.PreviousStudentId).FirstOrDefaultAsync(ct);
        }
        return chain;
    }
}
