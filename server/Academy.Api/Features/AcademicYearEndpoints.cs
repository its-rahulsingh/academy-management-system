using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// The academy's year master. Everyone can list the years (to switch between them);
/// only the Super Admin can add a year or change the dates of the current or a future one.
/// </summary>
public static class AcademicYearEndpoints
{
    private const int MaxYearDays = 731;

    public sealed record YearRequest(string? Name, DateOnly StartDate, DateOnly EndDate);

    public static void MapAcademicYearEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/academic-years");
        group.MapGet("", List).RequirePermission(Permissions.Workspace);
        group.MapPost("", Create).RequirePermission(Permissions.YearsManage);
        group.MapPut("/{id:guid}", Update).RequirePermission(Permissions.YearsManage);
    }

    private static async Task<IResult> List(AcademyDbContext db, HttpContext http, CancellationToken ct)
    {
        var today = AcademicYearScope.Today;
        var years = await db.AcademicYears.AsNoTracking()
            .OrderByDescending(y => y.StartDate)
            .Select(y => new { y.Id, y.Name, y.StartDate, y.EndDate, Students = db.Students.Count(s => s.AcademicYearId == y.Id) })
            .ToListAsync(ct);
        var currentId = AcademicYearScope.CurrentOf(years.Select(y => (y.Id, y.StartDate, y.EndDate)), today);

        return Results.Ok(new
        {
            currentId,
            canManage = http.User.Has(Permissions.YearsManage),
            items = years.Select(y => new
            {
                y.Id, y.Name, y.StartDate, y.EndDate, y.Students,
                isCurrent = y.Id == currentId,
                isClosed = y.EndDate < today
            })
        });
    }

    private static string? ValidateDates(YearRequest request, DateOnly today)
    {
        if (!Validate.Name(request.Name, 40)) return "Enter a name for the year, e.g. 2026-27.";
        if (request.StartDate == default || request.EndDate == default) return "Enter the start and end dates.";
        if (request.EndDate <= request.StartDate) return "The end date must be after the start date.";
        if (request.EndDate.DayNumber - request.StartDate.DayNumber > MaxYearDays) return "An academic year cannot be longer than two years.";
        if (request.EndDate < today) return "The end date cannot be in the past. Past years cannot be added or changed.";
        return null;
    }

    private static Task<bool> OverlapsAsync(AcademyDbContext db, YearRequest request, Guid? exceptId, CancellationToken ct) =>
        db.AcademicYears.AnyAsync(y => y.Id != exceptId && y.StartDate <= request.EndDate && request.StartDate <= y.EndDate, ct);

    private static Task<bool> NameTakenAsync(AcademyDbContext db, string name, Guid? exceptId, CancellationToken ct) =>
        db.AcademicYears.AnyAsync(y => y.Id != exceptId && y.Name.ToLower() == name.ToLower(), ct);

    private static async Task<IResult> Create(YearRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (ValidateDates(request, AcademicYearScope.Today) is { } error) return ApiResults.Bad(error);
        var name = request.Name!.Trim();
        if (await NameTakenAsync(db, name, null, ct)) return ApiResults.Conflict("A year with that name already exists.");
        if (await OverlapsAsync(db, request, null, ct)) return ApiResults.Conflict("These dates overlap another academic year.");

        var year = new AcademicYear { Name = name, StartDate = request.StartDate, EndDate = request.EndDate };
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/academic-years/{year.Id}", new { year.Id });
    }

    private static async Task<IResult> Update(Guid id, YearRequest request, AcademyDbContext db, CancellationToken ct)
    {
        var today = AcademicYearScope.Today;
        var year = await db.AcademicYears.FirstOrDefaultAsync(y => y.Id == id, ct);
        if (year is null) return Results.NotFound();
        if (year.IsClosedOn(today)) return ApiResults.Bad("This academic year has ended. Its dates can no longer be changed.");
        if (ValidateDates(request, today) is { } error) return ApiResults.Bad(error);

        var name = request.Name!.Trim();
        if (await NameTakenAsync(db, name, id, ct)) return ApiResults.Conflict("A year with that name already exists.");
        if (await OverlapsAsync(db, request, id, ct)) return ApiResults.Conflict("These dates overlap another academic year.");

        year.Name = name;
        year.StartDate = request.StartDate;
        year.EndDate = request.EndDate;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { year.Id });
    }
}
