using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>The academy's subject master. Subjects are linked to courses and faculty, and every test is set in one.</summary>
public static class SubjectEndpoints
{
    public sealed record SubjectRequest(string? Name, string? Code, bool Active = true);

    public static void MapSubjectEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/subjects");
        group.MapGet("", List).RequirePermission(Permissions.CoursesRead);
        group.MapPost("", Create).RequirePermission(Permissions.CoursesWrite);
        group.MapPut("/{id:guid}", Update).RequirePermission(Permissions.CoursesWrite);
    }

    private static async Task<IResult> List(AcademyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Subjects.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Id, s.Name, s.Code, s.Active,
                Courses = db.CourseSubjects.Count(c => c.SubjectId == s.Id),
                Faculty = db.FacultySubjects.Count(f => f.SubjectId == s.Id),
                Tests = db.Tests.Count(t => t.SubjectId == s.Id)
            })
            .ToListAsync(ct));

    private static string? ValidateSubject(SubjectRequest request)
    {
        if (!Validate.Name(request.Name, Subject.MaxNameLength)) return "Enter the subject name.";
        if ((request.Code ?? "").Trim().Length > Subject.MaxCodeLength) return $"Keep the code to {Subject.MaxCodeLength} characters.";
        return null;
    }

    private static Task<bool> NameTakenAsync(AcademyDbContext db, string name, Guid? exceptId, CancellationToken ct) =>
        db.Subjects.AnyAsync(s => s.Id != exceptId && s.Name.ToLower() == name.ToLower(), ct);

    private static async Task<IResult> Create(SubjectRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (ValidateSubject(request) is { } error) return ApiResults.Bad(error);
        var name = request.Name!.Trim();
        if (await NameTakenAsync(db, name, null, ct)) return ApiResults.Conflict("A subject with that name already exists.");

        var subject = new Subject { Name = name, Code = Validate.Text(request.Code, Subject.MaxCodeLength).ToUpperInvariant(), Active = request.Active };
        db.Subjects.Add(subject);
        return await SaveAsync(db, Results.Created($"/api/subjects/{subject.Id}", new { subject.Id }), ct);
    }

    private static async Task<IResult> Update(Guid id, SubjectRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (ValidateSubject(request) is { } error) return ApiResults.Bad(error);
        var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subject is null) return Results.NotFound();
        var name = request.Name!.Trim();
        if (await NameTakenAsync(db, name, id, ct)) return ApiResults.Conflict("A subject with that name already exists.");

        // Renaming is safe: tests, courses and faculty refer to the subject, not to its name.
        subject.Name = name;
        subject.Code = Validate.Text(request.Code, Subject.MaxCodeLength).ToUpperInvariant();
        subject.Active = request.Active;
        return await SaveAsync(db, Results.Ok(new { subject.Id }), ct);
    }

    private static async Task<IResult> SaveAsync(AcademyDbContext db, IResult success, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return success;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return ApiResults.Conflict("A subject with that name already exists.");
        }
    }
}

/// <summary>Replaces the subjects linked to a course or a faculty member with a new set.</summary>
public static class SubjectLinks
{
    public const int MaxPerOwner = 50;

    /// <summary>
    /// Checks a requested set of subject ids. Subjects already linked may stay even if since made inactive;
    /// newly linked ones must be active. Returns the distinct ids, or an error.
    /// </summary>
    public static async Task<(IReadOnlyList<Guid> Ids, string? Error)> CheckAsync(
        AcademyDbContext db, IReadOnlyCollection<Guid> requested, IReadOnlyCollection<Guid> current, CancellationToken ct)
    {
        var ids = requested.Distinct().ToList();
        if (ids.Count > MaxPerOwner) return (ids, $"Link at most {MaxPerOwner} subjects.");

        // The tenant filter applies, so another academy's subject ids are simply not found.
        var found = await db.Subjects.Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.Active }).ToListAsync(ct);
        if (found.Count != ids.Count) return (ids, "One of the selected subjects no longer exists. Reload and try again.");
        if (found.Any(s => !s.Active && !current.Contains(s.Id))) return (ids, "Inactive subjects cannot be linked. Reactivate the subject first.");
        return (ids, null);
    }

    public static async Task<string?> SetCourseSubjectsAsync(AcademyDbContext db, Guid courseId, IReadOnlyCollection<Guid> requested, CancellationToken ct)
    {
        var links = await db.CourseSubjects.Where(l => l.CourseId == courseId).ToListAsync(ct);
        var (ids, error) = await CheckAsync(db, requested, links.Select(l => l.SubjectId).ToList(), ct);
        if (error is not null) return error;

        var removed = links.Where(l => !ids.Contains(l.SubjectId)).ToList();
        var removedIds = removed.Select(l => l.SubjectId).ToList();
        // A subject with tests in this course stays linked, or those tests would sit in a subject the course does not teach.
        var inUse = await db.Tests.Where(t => t.CourseId == courseId && removedIds.Contains(t.SubjectId))
            .Select(t => t.Subject.Name).Distinct().ToListAsync(ct);
        if (inUse.Count > 0)
            return $"{string.Join(", ", inUse)} already {(inUse.Count == 1 ? "has" : "have")} tests in this course, so it cannot be removed.";

        db.CourseSubjects.RemoveRange(removed);
        foreach (var id in ids.Where(id => links.All(l => l.SubjectId != id)))
            db.CourseSubjects.Add(new CourseSubject { CourseId = courseId, SubjectId = id });
        return null;
    }

    public static async Task<string?> SetFacultySubjectsAsync(AcademyDbContext db, Guid facultyId, IReadOnlyCollection<Guid> requested, CancellationToken ct)
    {
        var links = await db.FacultySubjects.Where(l => l.FacultyId == facultyId).ToListAsync(ct);
        var (ids, error) = await CheckAsync(db, requested, links.Select(l => l.SubjectId).ToList(), ct);
        if (error is not null) return error;

        // Past teaching sessions keep their subject even if the faculty member no longer teaches it.
        db.FacultySubjects.RemoveRange(links.Where(l => !ids.Contains(l.SubjectId)));
        foreach (var id in ids.Where(id => links.All(l => l.SubjectId != id)))
            db.FacultySubjects.Add(new FacultySubject { FacultyId = facultyId, SubjectId = id });
        return null;
    }
}
