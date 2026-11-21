using System.Globalization;
using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// A faculty member's own portal. Everything here is scoped to the faculty record linked to the signed-in login,
/// so a faculty member can only ever see and submit their own sessions and pay.
/// Teaching time comes from clocking in and out (see AttendanceEndpoints).
/// </summary>
public static class FacultyPortalEndpoints
{
    /// <summary>How a session looks to the faculty member.</summary>
    public enum SessionState
    {
        Waiting,
        /// <summary>Approved and not yet covered by payouts.</summary>
        Approved,
        Paid,
        Rejected,
        Withdrawn
    }

    private const int MaxListed = 500;

    public static void MapFacultyPortalEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/me/faculty").RequirePermission(Permissions.FacultyPortal);
        group.MapGet("", Overview);
        group.MapGet("/sessions", Sessions);
        group.MapPost("/sessions/{id:guid}/withdraw", Withdraw);
        group.MapGet("/payments", Payments);
    }

    private static async Task<Faculty?> CurrentFacultyAsync(AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var facultyId = await db.Users.Where(u => u.Id == currentUser.UserId).Select(u => u.FacultyId).FirstOrDefaultAsync(ct);
        return facultyId is null ? null : await db.Faculty.AsNoTracking().FirstOrDefaultAsync(f => f.Id == facultyId, ct);
    }

    private static IResult NotLinked() =>
        ApiResults.Error(StatusCodes.Status403Forbidden, "This login is not linked to a faculty record. Ask the academy to check it.");

    /// <summary>"2026-11" → first day of that month; anything else → this month.</summary>
    private static DateOnly MonthStart(string? month) =>
        DateOnly.TryParseExact($"{month}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            ? start
            : new DateOnly(Validate.Today.Year, Validate.Today.Month, 1);

    private sealed record Row(
        Guid Id, DateOnly TaughtOn, TimeOnly? StartTime, TimeOnly? EndTime, int Minutes, decimal HourlyRate, decimal Amount,
        string? SubjectName, string Topic, LedgerStatus Status, SessionReview Review, string ReviewNote, string? VoidReason,
        DateTime CreatedAtUtc, bool LoggedByStaff);

    /// <summary>Filter first, then project: EF cannot translate a filter on a constructor-built record.</summary>
    private static IQueryable<Row> Project(this IQueryable<FacultySession> sessions, Guid? userId) =>
        sessions.AsNoTracking().Select(s => new Row(
            s.Id, s.TaughtOn, s.StartTime, s.EndTime, s.Minutes, s.HourlyRate, s.Amount,
            s.Subject != null ? s.Subject.Name : null, s.Topic, s.Status, s.Review, s.ReviewNote, s.VoidReason,
            s.CreatedAtUtc, s.CreatedByUserId != userId));

    /// <summary>
    /// Payouts are not tied to sessions, so which sessions are paid is worked out oldest first:
    /// approved sessions are covered by the total paid out until it runs out.
    /// A session the money only partly covers still shows as approved (unpaid).
    /// </summary>
    private static async Task<HashSet<Guid>> PaidSessionIdsAsync(AcademyDbContext db, Faculty faculty, decimal paid, CancellationToken ct)
    {
        // Package faculty are paid their package, not per class, so none of their classes is "paid".
        if (faculty.PayType == FacultyPayType.Package) return [];
        var facultyId = faculty.Id;
        var counting = await db.FacultySessions.AsNoTracking().Counting().Where(s => s.FacultyId == facultyId)
            .OrderBy(s => s.TaughtOn).ThenBy(s => s.CreatedAtUtc)
            .Select(s => new { s.Id, s.Amount })
            .ToListAsync(ct);
        var result = new HashSet<Guid>();
        var left = paid;
        foreach (var s in counting)
        {
            if (s.Amount > left) break;
            left -= s.Amount;
            result.Add(s.Id);
        }
        return result;
    }

    private static SessionState StateOf(Row row, HashSet<Guid> paid) =>
        row.Status == LedgerStatus.Voided ? SessionState.Withdrawn
        : row.Review == SessionReview.Pending ? SessionState.Waiting
        : row.Review == SessionReview.Rejected ? SessionState.Rejected
        : paid.Contains(row.Id) ? SessionState.Paid
        : SessionState.Approved;

    private static object Shape(Row r, SessionState state) => new
    {
        r.Id, r.TaughtOn, r.StartTime, r.EndTime, r.Minutes, r.HourlyRate, r.Amount, r.SubjectName, r.Topic, State = state,
        Note = state == SessionState.Withdrawn ? r.VoidReason : r.ReviewNote,
        r.LoggedByStaff
    };

    // ---- Overview ----

    private static async Task<IResult> Overview(string? month, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (await CurrentFacultyAsync(db, currentUser, ct) is not { } faculty) return NotLinked();
        var start = MonthStart(month);
        var end = start.AddMonths(1).AddDays(-1);

        var paid = await FacultyEndpoints.PaidToAsync(db, faculty.Id, ct);
        var earned = await FacultyEndpoints.EarnedAsync(db, faculty, ct);
        var paidIds = await PaidSessionIdsAsync(db, faculty, paid, ct);
        var monthRows = await db.FacultySessions
            .Where(s => s.FacultyId == faculty.Id && s.TaughtOn >= start && s.TaughtOn <= end && s.Status == LedgerStatus.Posted)
            .Project(currentUser.UserId)
            .ToListAsync(ct);
        var waiting = await db.FacultySessions.Where(s => s.FacultyId == faculty.Id && s.Status == LedgerStatus.Posted && s.Review == SessionReview.Pending)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Amount = g.Sum(s => s.Amount) }).FirstOrDefaultAsync(ct);
        var counted = monthRows.Where(r => r.Review == SessionReview.Approved).ToList();
        var monthWaiting = monthRows.Where(r => r.Review == SessionReview.Pending).ToList();
        var lastPayout = await db.FacultyPayouts.AsNoTracking()
            .Where(p => p.FacultyId == faculty.Id && p.Status == LedgerStatus.Posted)
            .OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.CreatedAtUtc)
            .Select(p => new { p.Amount, p.PaidOn, p.Method })
            .FirstOrDefaultAsync(ct);
        var unpaidApproved = faculty.PayType == FacultyPayType.Hourly
            ? await db.FacultySessions.Counting().Where(s => s.FacultyId == faculty.Id).CountAsync(ct) - paidIds.Count
            : 0;

        var subjects = await db.FacultySubjects.AsNoTracking().Where(l => l.FacultyId == faculty.Id)
            .OrderBy(l => l.Subject.Name).Select(l => new { l.Subject.Id, l.Subject.Name, l.Subject.Active }).ToListAsync(ct);
        var courses = await db.CourseSubjects.AsNoTracking()
            .Where(cs => db.FacultySubjects.Any(l => l.FacultyId == faculty.Id && l.SubjectId == cs.SubjectId))
            .Join(db.Courses, cs => cs.CourseId, c => c.Id, (cs, c) => new { cs.SubjectId, c.Name })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            faculty = new { faculty.Id, faculty.FullName, faculty.PayType, faculty.HourlyRate, faculty.AgreedPayout, faculty.Active },
            month = start.ToString("yyyy-MM"),
            thisMonth = new
            {
                minutes = counted.Sum(r => r.Minutes),
                sessions = counted.Count,
                days = counted.Select(r => r.TaughtOn).Distinct().Count(),
                earned = counted.Sum(r => r.Amount),
                waitingMinutes = monthWaiting.Sum(r => r.Minutes),
                waitingSessions = monthWaiting.Count
            },
            totals = new
            {
                earned,
                paid,
                owed = Math.Max(0, earned - paid),
                unpaidSessions = Math.Max(0, unpaidApproved),
                waitingSessions = waiting?.Count ?? 0,
                waitingAmount = waiting?.Amount ?? 0,
                lastPayout
            },
            subjects = subjects.Select(s => new
            {
                s.Id, s.Name, s.Active,
                courses = courses.Where(c => c.SubjectId == s.Id).Select(c => c.Name).Distinct().Order().ToList(),
                minutesThisMonth = monthRows.Where(r => r.SubjectName == s.Name && r.Review != SessionReview.Rejected).Sum(r => r.Minutes)
            })
        });
    }

    // ---- Sessions ----

    private static async Task<IResult> Sessions(
        string? month, string? view, string? search, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (await CurrentFacultyAsync(db, currentUser, ct) is not { } faculty) return NotLinked();
        var start = MonthStart(month);
        var end = start.AddMonths(1).AddDays(-1);

        var rows = db.FacultySessions.Where(s => s.FacultyId == faculty.Id && s.TaughtOn >= start && s.TaughtOn <= end);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = QueryExtensions.Like(search);
            rows = rows.Where(s => EF.Functions.ILike(s.Topic, like) || (s.Subject != null && EF.Functions.ILike(s.Subject.Name, like)));
        }
        var list = await rows.OrderByDescending(s => s.TaughtOn).ThenByDescending(s => s.StartTime).ThenByDescending(s => s.CreatedAtUtc)
            .Take(MaxListed).Project(currentUser.UserId).ToListAsync(ct);

        var paidIds = await PaidSessionIdsAsync(db, faculty, await FacultyEndpoints.PaidToAsync(db, faculty.Id, ct), ct);
        var shaped = list.Select(r => (Row: r, State: StateOf(r, paidIds))).ToList();

        // "Deleted history" holds what was withdrawn or rejected; the other tabs hold live sessions.
        static bool Live(SessionState s) => s is SessionState.Waiting or SessionState.Approved or SessionState.Paid;
        var counts = new
        {
            all = shaped.Count(x => Live(x.State)),
            waiting = shaped.Count(x => x.State == SessionState.Waiting),
            approved = shaped.Count(x => x.State == SessionState.Approved),
            paid = shaped.Count(x => x.State == SessionState.Paid),
            removed = shaped.Count(x => !Live(x.State))
        };
        var selected = (view ?? "all").ToLowerInvariant() switch
        {
            "waiting" => shaped.Where(x => x.State == SessionState.Waiting),
            "approved" => shaped.Where(x => x.State == SessionState.Approved),
            "paid" => shaped.Where(x => x.State == SessionState.Paid),
            "removed" => shaped.Where(x => !Live(x.State)),
            _ => shaped.Where(x => Live(x.State))
        };

        return Results.Ok(new { month = start.ToString("yyyy-MM"), counts, items = selected.Select(x => Shape(x.Row, x.State)) });
    }

    private static async Task<IResult> Withdraw(Guid id, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (await CurrentFacultyAsync(db, currentUser, ct) is not { } faculty) return NotLinked();
        var session = await db.FacultySessions.FirstOrDefaultAsync(s => s.Id == id && s.FacultyId == faculty.Id, ct);
        if (session is null) return Results.NotFound();
        if (session.Status == LedgerStatus.Voided) return ApiResults.Conflict("This session was already withdrawn.");
        if (session.Review != SessionReview.Pending)
            return ApiResults.Conflict("Only sessions still waiting for approval can be withdrawn. Ask the academy to correct an approved one.");

        session.Void(currentUser.UserId, "Withdrawn by the faculty member");
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    // ---- Payments ----

    private static async Task<IResult> Payments(AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (await CurrentFacultyAsync(db, currentUser, ct) is not { } faculty) return NotLinked();
        var paid = await FacultyEndpoints.PaidToAsync(db, faculty.Id, ct);
        var earned = await FacultyEndpoints.EarnedAsync(db, faculty, ct);
        var paidIds = await PaidSessionIdsAsync(db, faculty, paid, ct);

        var counting = await db.FacultySessions.Counting().Where(s => s.FacultyId == faculty.Id)
            .OrderByDescending(s => s.TaughtOn).ThenByDescending(s => s.CreatedAtUtc)
            .Take(MaxListed * 2).Project(currentUser.UserId).ToListAsync(ct);
        var waiting = await db.FacultySessions.Where(s => s.FacultyId == faculty.Id && s.Status == LedgerStatus.Posted && s.Review == SessionReview.Pending)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Amount = g.Sum(s => s.Amount) }).FirstOrDefaultAsync(ct);
        var payouts = await db.FacultyPayouts.AsNoTracking()
            .Where(p => p.FacultyId == faculty.Id && p.Status == LedgerStatus.Posted)
            .OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.CreatedAtUtc)
            .Take(MaxListed)
            .Select(p => new { p.Id, p.Amount, p.PaidOn, p.Method, p.Notes })
            .ToListAsync(ct);
        var rates = await db.FacultySessions.Counting().Where(s => s.FacultyId == faculty.Id)
            .GroupBy(s => s.HourlyRate)
            .Select(g => new { HourlyRate = g.Key, From = g.Min(s => s.TaughtOn), To = g.Max(s => s.TaughtOn), Sessions = g.Count() })
            .OrderByDescending(r => r.To)
            .ToListAsync(ct);

        var unpaid = counting.Where(r => !paidIds.Contains(r.Id)).ToList();
        return Results.Ok(new
        {
            faculty = new { faculty.FullName, faculty.PayType, faculty.HourlyRate, faculty.AgreedPayout },
            unpaid = Math.Max(0, earned - paid),
            unpaidSessions = unpaid.Count,
            received = paid,
            paidSessions = paidIds.Count,
            waitingAmount = waiting?.Amount ?? 0,
            waitingSessions = waiting?.Count ?? 0,
            lastPayout = payouts.FirstOrDefault(),
            unpaidItems = unpaid.Select(r => Shape(r, SessionState.Approved)),
            paidItems = counting.Where(r => paidIds.Contains(r.Id)).Select(r => Shape(r, SessionState.Paid)),
            payouts,
            rates
        });
    }
}
