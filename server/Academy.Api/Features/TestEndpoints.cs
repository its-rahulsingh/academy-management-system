using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

public static class TestEndpoints
{
    public sealed record TestRequest(string? Title, string? Subject, Guid CourseId, decimal MaxMarks, DateOnly? TestDate, string? Notes);
    public sealed record ResultRequest(decimal Marks, string? Remarks);

    public sealed class TestListQuery : PageQuery
    {
        public Guid? CourseId { get; set; }
    }

    public sealed class ReportCardQuery : PageQuery
    {
        public string? Search { get; set; }
    }

    public static void MapTestEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/tests");
        group.MapGet("", List).RequirePermission(Permissions.TestsRead);
        group.MapGet("/report-cards", ReportCards).RequirePermission(Permissions.TestsRead);
        group.MapGet("/{id:guid}/results", Results_).RequirePermission(Permissions.TestsRead);
        group.MapPost("", Create).RequirePermission(Permissions.TestsWrite);
        group.MapPut("/{id:guid}/results/{studentId:guid}", SaveResult).RequirePermission(Permissions.TestsWrite);
    }

    private static async Task<IResult> List([AsParameters] TestListQuery query, AcademyDbContext db, CancellationToken ct)
    {
        var tests = db.Tests.AsNoTracking();
        if (query.CourseId is { } courseId) tests = tests.Where(t => t.CourseId == courseId);

        return Results.Ok(await tests
            .OrderByDescending(t => t.TestDate).ThenByDescending(t => t.CreatedAtUtc)
            .Select(t => new
            {
                t.Id, t.Title, t.Subject, t.CourseId, CourseName = t.Course.Name, t.MaxMarks, t.TestDate, t.Notes,
                ResultCount = db.TestResults.Count(r => r.TestId == t.Id),
                AverageMarks = db.TestResults.Where(r => r.TestId == t.Id).Average(r => (decimal?)r.Marks)
            })
            .ToPagedAsync(query, ct));
    }

    private static async Task<IResult> Results_(Guid id, AcademyDbContext db, CancellationToken ct)
    {
        if (!await db.Tests.AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
        return Results.Ok(await db.TestResults.AsNoTracking()
            .Where(r => r.TestId == id)
            .OrderBy(r => r.Student.FullName)
            .Select(r => new { r.Id, r.StudentId, StudentName = r.Student.FullName, r.Marks, r.Remarks })
            .ToListAsync(ct));
    }

    private static async Task<IResult> ReportCards([AsParameters] ReportCardQuery query, AcademyDbContext db, CancellationToken ct)
    {
        var students = db.Students.AsNoTracking().Where(s => s.Active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = QueryExtensions.Like(query.Search);
            students = students.Where(s => EF.Functions.ILike(s.FullName, like));
        }

        return Results.Ok(await students
            .OrderBy(s => s.FullName)
            .Select(s => new
            {
                StudentId = s.Id, s.FullName, CourseName = s.Course.Name,
                TestsTaken = db.TestResults.Count(r => r.StudentId == s.Id),
                AveragePercent = db.TestResults.Where(r => r.StudentId == s.Id).Average(r => (decimal?)(100 * r.Marks / r.Test.MaxMarks))
            })
            .ToPagedAsync(query, ct));
    }

    private static async Task<IResult> Create(TestRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (!Validate.Name(request.Title, 180)) return ApiResults.Bad("Enter a test title.");
        if (request.MaxMarks is <= 0 or > 100_000) return ApiResults.Bad("Enter positive maximum marks.");
        if (!await db.Courses.AnyAsync(c => c.Id == request.CourseId, ct)) return ApiResults.Bad("Course not found.");

        var test = new AcademyTest
        {
            Title = request.Title!.Trim(),
            Subject = Validate.Text(request.Subject, 100),
            CourseId = request.CourseId,
            MaxMarks = request.MaxMarks,
            TestDate = Validate.DateOrToday(request.TestDate),
            Notes = Validate.Text(request.Notes, 1200)
        };
        db.Tests.Add(test);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/tests/{test.Id}", new { test.Id });
    }

    private static async Task<IResult> SaveResult(Guid id, Guid studentId, ResultRequest request, AcademyDbContext db, CancellationToken ct)
    {
        var test = await db.Tests.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (test is null || student is null) return Results.NotFound();
        if (student.CourseId != test.CourseId) return ApiResults.Bad("Student is not enrolled in this test's course.");
        if (request.Marks < 0 || request.Marks > test.MaxMarks) return ApiResults.Bad($"Marks must be between 0 and {test.MaxMarks:0.##}.");

        var result = await db.TestResults.FirstOrDefaultAsync(r => r.TestId == id && r.StudentId == studentId, ct);
        if (result is null)
        {
            result = new TestResult { TestId = id, StudentId = studentId };
            db.TestResults.Add(result);
        }
        result.Marks = request.Marks;
        result.Remarks = Validate.Text(request.Remarks, 600);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return ApiResults.Conflict("Someone else just saved a result for this student. Reload and try again.");
        }
        return Results.Ok(new { result.Id });
    }
}
