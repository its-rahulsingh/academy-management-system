using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

public static class CourseEndpoints
{
    public sealed record CourseRequest(string? Name, decimal Fee, bool Active = true);

    public static void MapCourseEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/courses");
        group.MapGet("", List).RequirePermission(Permissions.CoursesRead);
        group.MapPost("", Create).RequirePermission(Permissions.CoursesWrite);
        group.MapPut("/{id:guid}", Update).RequirePermission(Permissions.CoursesWrite);
    }

    /// <summary>The course catalogue is shared by all years; enrolment and collection are for the selected year's students.</summary>
    private static async Task<IResult> List(AcademyDbContext db, AcademicYearScope years, CancellationToken ct)
    {
        var yearId = (await years.GetAsync(ct))?.Id;
        return Results.Ok(await db.Courses.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                c.Id, c.Name, c.Fee, c.Active,
                Enrolled = db.Students.Count(s => s.CourseId == c.Id && (yearId == null || s.AcademicYearId == yearId)),
                Collected = (db.StudentPayments
                        .Where(p => p.Student.CourseId == c.Id && (yearId == null || p.Student.AcademicYearId == yearId)
                                    && p.Status == LedgerStatus.Posted && p.Kind == PaymentKind.Payment)
                        .Sum(p => (decimal?)p.Amount) ?? 0)
                    - (db.StudentPayments
                        .Where(p => p.Student.CourseId == c.Id && (yearId == null || p.Student.AcademicYearId == yearId)
                                    && p.Status == LedgerStatus.Posted && p.Kind == PaymentKind.Refund)
                        .Sum(p => (decimal?)p.Amount) ?? 0)
            })
            .ToListAsync(ct));
    }

    private static async Task<IResult> Create(CourseRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (!Validate.Name(request.Name) || !Validate.AmountOrZero(request.Fee))
            return ApiResults.Bad("Enter a course name and a valid fee.");
        var name = request.Name!.Trim();
        if (await NameTakenAsync(db, name, null, ct)) return ApiResults.Conflict("A course with that name already exists.");

        var course = new Course { Name = name, Fee = request.Fee, Active = request.Active };
        db.Courses.Add(course);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/courses/{course.Id}", new { course.Id });
    }

    private static async Task<IResult> Update(Guid id, CourseRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (!Validate.Name(request.Name) || !Validate.AmountOrZero(request.Fee))
            return ApiResults.Bad("Enter a course name and a valid fee.");
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course is null) return Results.NotFound();
        var name = request.Name!.Trim();
        if (await NameTakenAsync(db, name, id, ct)) return ApiResults.Conflict("A course with that name already exists.");

        // Changing the fee affects future admissions only: each student keeps the fee agreed at admission.
        course.Name = name;
        course.Fee = request.Fee;
        course.Active = request.Active;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { course.Id });
    }

    private static Task<bool> NameTakenAsync(AcademyDbContext db, string name, Guid? exceptId, CancellationToken ct) =>
        db.Courses.AnyAsync(c => c.Id != exceptId && c.Name.ToLower() == name.ToLower(), ct);
}
