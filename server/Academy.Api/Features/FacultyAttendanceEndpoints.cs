using System.Globalization;
using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Web;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// A month's attendance for every faculty member: classes taught, time, and the days they came in. Counts only
/// approved classes; those still waiting for approval are shown alongside. Useful above all for package faculty,
/// who are not paid per class but whose attendance still matters.
/// </summary>
public static class FacultyAttendanceEndpoints
{
    public static void MapFacultyAttendanceEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/faculty/attendance").RequirePermission(Permissions.FacultyRead);
        group.MapGet("", Get);
        group.MapGet("/excel", Download);
    }

    private sealed record Row(Guid FacultyId, string Name, FacultyPayType PayType, bool Active, int Classes, int Minutes, int Days, int Waiting);

    private static (DateOnly Start, DateOnly End) Month(string? month)
    {
        var start = DateOnly.TryParseExact($"{month}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : new DateOnly(Validate.Today.Year, Validate.Today.Month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    private static async Task<List<Row>> LoadAsync(AcademyDbContext db, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var sessions = await db.FacultySessions.AsNoTracking()
            .Where(s => s.TaughtOn >= start && s.TaughtOn <= end && s.Status == LedgerStatus.Posted && s.Review != SessionReview.Rejected)
            .Select(s => new { s.FacultyId, s.TaughtOn, s.Minutes, s.Review })
            .ToListAsync(ct);
        var faculty = await db.Faculty.AsNoTracking().OrderBy(f => f.FullName)
            .Select(f => new { f.Id, f.FullName, f.PayType, f.Active }).ToListAsync(ct);

        return faculty
            .Select(f =>
            {
                var mine = sessions.Where(s => s.FacultyId == f.Id).ToList();
                var approved = mine.Where(s => s.Review == SessionReview.Approved).ToList();
                return new Row(f.Id, f.FullName, f.PayType, f.Active, approved.Count, approved.Sum(s => s.Minutes),
                    approved.Select(s => s.TaughtOn).Distinct().Count(), mine.Count(s => s.Review == SessionReview.Pending));
            })
            // Former faculty appear only for months they actually taught in.
            .Where(r => r.Active || r.Classes + r.Waiting > 0)
            .ToList();
    }

    private static async Task<IResult> Get(string? month, AcademyDbContext db, CancellationToken ct)
    {
        var (start, end) = Month(month);
        var rows = await LoadAsync(db, start, end, ct);
        return Results.Ok(new { month = start.ToString("yyyy-MM"), from = start, to = end, items = rows });
    }

    private static async Task<IResult> Download(string? month, AcademyDbContext db, CancellationToken ct)
    {
        var (start, end) = Month(month);
        var rows = await LoadAsync(db, start, end, ct);
        using var book = new XLWorkbook();
        var sheet = Excel.Sheet(book, start.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            ["Faculty", "Pay", "Days present", "Classes", "Time taught (min)", "Time taught (hours)", "Waiting for approval"],
            rows.Select(r => new object?[] { r.Name, r.PayType.ToString(), r.Days, r.Classes, r.Minutes, Math.Round(r.Minutes / 60m, 2), r.Waiting }));
        Excel.TotalRow(sheet,
            ["Total", "", "", rows.Sum(r => r.Classes), rows.Sum(r => r.Minutes), Math.Round(rows.Sum(r => r.Minutes) / 60m, 2), rows.Sum(r => r.Waiting)],
            new HashSet<int>());
        return Excel.File(book, $"faculty-attendance-{start:yyyy-MM}.xlsx");
    }
}
