using Academy.Api.Data;
using Academy.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Infrastructure.Tenancy;

/// <summary>The academic year a request is looking at.</summary>
public sealed record YearRange(Guid Id, string Name, DateOnly Start, DateOnly End, bool IsClosed, bool IsCurrent)
{
    public bool Contains(DateOnly date) => date >= Start && date <= End;
}

/// <summary>
/// Resolves the academic year chosen in the app's global year switch. The browser sends it in the
/// X-Academic-Year header (or academicYearId query parameter, for download links). Without one,
/// or with an id that is not this academy's, the current year is used.
/// </summary>
public sealed class AcademicYearScope(AcademyDbContext db, IHttpContextAccessor http)
{
    public const string HeaderName = "X-Academic-Year";
    public const string QueryName = "academicYearId";

    private YearRange? _resolved;
    private bool _loaded;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>The selected year, or null if the academy has no years yet.</summary>
    public async Task<YearRange?> GetAsync(CancellationToken ct)
    {
        if (_loaded) return _resolved;

        // The tenant query filter limits this to the caller's academy, so a foreign id simply finds nothing.
        var years = await db.AcademicYears.AsNoTracking()
            .Select(y => new { y.Id, y.Name, y.StartDate, y.EndDate })
            .ToListAsync(ct);
        var today = Today;
        var current = CurrentOf(years.Select(y => (y.Id, y.StartDate, y.EndDate)), today);
        var requested = RequestedId();
        var chosen = years.FirstOrDefault(y => y.Id == requested) ?? years.FirstOrDefault(y => y.Id == current);

        _resolved = chosen is null
            ? null
            : new YearRange(chosen.Id, chosen.Name, chosen.StartDate, chosen.EndDate, chosen.EndDate < today, chosen.Id == current);
        _loaded = true;
        return _resolved;
    }

    /// <summary>
    /// The current year is the one containing today. Between years (a gap in the calendar) it is the most
    /// recent one that has started; before the first year starts, it is the earliest.
    /// </summary>
    public static Guid? CurrentOf(IEnumerable<(Guid Id, DateOnly Start, DateOnly End)> years, DateOnly today)
    {
        var list = years.ToList();
        return list.Where(y => y.Start <= today && today <= y.End).Select(y => (Guid?)y.Id).FirstOrDefault()
            ?? list.Where(y => y.Start <= today).OrderByDescending(y => y.Start).Select(y => (Guid?)y.Id).FirstOrDefault()
            ?? list.OrderBy(y => y.Start).Select(y => (Guid?)y.Id).FirstOrDefault();
    }

    private Guid? RequestedId()
    {
        var request = http.HttpContext?.Request;
        if (request is null) return null;
        var value = request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(value)) value = request.Query[QueryName].ToString();
        return Guid.TryParse(value, out var id) ? id : null;
    }
}

public static class YearQueryExtensions
{
    /// <summary>Students admitted in the year. No year (an academy not set up yet) means no filter.</summary>
    public static IQueryable<Student> InYear(this IQueryable<Student> students, YearRange? year) =>
        year is null ? students : students.Where(s => s.AcademicYearId == year.Id);

    /// <summary>Dated records (payments, expenses, payouts, tests) whose date falls inside the year.</summary>
    public static IQueryable<T> Between<T>(this IQueryable<T> rows, YearRange? year, System.Linq.Expressions.Expression<Func<T, DateOnly>> date)
    {
        if (year is null) return rows;
        var start = year.Start;
        var end = year.End;
        var parameter = date.Parameters[0];
        var body = System.Linq.Expressions.Expression.AndAlso(
            System.Linq.Expressions.Expression.GreaterThanOrEqual(date.Body, System.Linq.Expressions.Expression.Constant(start)),
            System.Linq.Expressions.Expression.LessThanOrEqual(date.Body, System.Linq.Expressions.Expression.Constant(end)));
        return rows.Where(System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, parameter));
    }
}
