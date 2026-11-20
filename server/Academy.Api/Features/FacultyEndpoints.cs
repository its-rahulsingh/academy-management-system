using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// Faculty and what the academy owes them. A faculty member is paid either a fixed package or by the hour;
/// for hourly faculty, what they have earned is the sum of the sessions logged for them.
/// Either way: owed = earned − paid, and nothing may make it negative.
/// </summary>
public static class FacultyEndpoints
{
    public sealed record FacultyRequest(
        string? FullName, string? Email, string? Phone, string? Specialization, decimal AgreedPayout,
        FacultyPayType PayType = FacultyPayType.Package, decimal HourlyRate = 0, bool Active = true,
        IReadOnlyList<Guid>? SubjectIds = null);
    public sealed record PayoutRequest(Guid FacultyId, decimal Amount, string? Method, DateOnly? PaidOn, string? Notes);
    public sealed record SessionRequest(Guid FacultyId, DateOnly? TaughtOn, int Minutes, string? Topic, Guid? SubjectId = null);
    public sealed record VoidRequest(string? Reason);

    public sealed class PayoutListQuery : PageQuery
    {
        public Guid? FacultyId { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
    }

    public sealed class SessionListQuery : PageQuery
    {
        public Guid? FacultyId { get; set; }
    }

    public static void MapFacultyEndpoints(this RouteGroupBuilder academy)
    {
        var faculty = academy.MapGroup("/faculty");
        faculty.MapGet("", List).RequirePermission(Permissions.FacultyRead);
        faculty.MapPost("", Create).RequirePermission(Permissions.FacultyWrite);
        faculty.MapPut("/{id:guid}", Update).RequirePermission(Permissions.FacultyWrite);

        var payouts = academy.MapGroup("/payouts");
        payouts.MapGet("", ListPayouts).RequirePermission(Permissions.PayoutsRead);
        payouts.MapPost("", RecordPayout).RequirePermission(Permissions.PayoutsWrite);
        payouts.MapPost("/{id:guid}/void", VoidPayout).RequirePermission(Permissions.LedgerCorrect);

        // Logging time creates money owed, so it sits with payouts rather than with editing faculty details.
        var sessions = academy.MapGroup("/faculty-sessions");
        sessions.MapGet("", ListSessions).RequirePermission(Permissions.PayoutsRead);
        sessions.MapPost("", LogSession).RequirePermission(Permissions.PayoutsWrite);
        sessions.MapPost("/{id:guid}/void", VoidSession).RequirePermission(Permissions.LedgerCorrect);
    }

    // ---- Balances ----

    private static async Task<decimal> PaidToAsync(AcademyDbContext db, Guid facultyId, CancellationToken ct) =>
        await db.FacultyPayouts.Where(p => p.FacultyId == facultyId && p.Status == LedgerStatus.Posted).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;

    private static async Task<decimal> SessionEarningsAsync(AcademyDbContext db, Guid facultyId, CancellationToken ct) =>
        await db.FacultySessions.Where(s => s.FacultyId == facultyId && s.Status == LedgerStatus.Posted).SumAsync(s => (decimal?)s.Amount, ct) ?? 0;

    private static async Task<decimal> EarnedAsync(AcademyDbContext db, Faculty faculty, CancellationToken ct) =>
        faculty.PayType == FacultyPayType.Package ? faculty.AgreedPayout : await SessionEarningsAsync(db, faculty.Id, ct);

    /// <summary>Everything every faculty member has earned to date: packages plus logged hourly sessions.</summary>
    public static async Task<decimal> TotalEarnedAsync(AcademyDbContext db, CancellationToken ct)
    {
        var packages = await db.Faculty.Where(f => f.PayType == FacultyPayType.Package).SumAsync(f => (decimal?)f.AgreedPayout, ct) ?? 0;
        var sessions = await db.FacultySessions
            .Where(s => s.Status == LedgerStatus.Posted && s.Faculty.PayType == FacultyPayType.Hourly)
            .SumAsync(s => (decimal?)s.Amount, ct) ?? 0;
        return packages + sessions;
    }

    private static async Task<IResult> List(AcademyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Faculty.AsNoTracking()
            .OrderBy(f => f.FullName)
            .Select(f => new
            {
                f.Id, f.FullName, f.Email, f.Phone, f.Specialization, f.PayType, f.AgreedPayout, f.HourlyRate, f.Active,
                Subjects = db.FacultySubjects.Where(l => l.FacultyId == f.Id)
                    .OrderBy(l => l.Subject.Name)
                    .Select(l => new { l.Subject.Id, l.Subject.Name, l.Subject.Code, l.Subject.Active })
                    .ToList(),
                MinutesTaught = db.FacultySessions.Where(s => s.FacultyId == f.Id && s.Status == LedgerStatus.Posted).Sum(s => (int?)s.Minutes) ?? 0,
                Earned = f.PayType == FacultyPayType.Package
                    ? f.AgreedPayout
                    : db.FacultySessions.Where(s => s.FacultyId == f.Id && s.Status == LedgerStatus.Posted).Sum(s => (decimal?)s.Amount) ?? 0,
                Paid = db.FacultyPayouts.Where(p => p.FacultyId == f.Id && p.Status == LedgerStatus.Posted).Sum(p => (decimal?)p.Amount) ?? 0
            })
            .ToListAsync(ct));

    // ---- Faculty ----

    private static string? ValidateFaculty(FacultyRequest request)
    {
        if (!Validate.Name(request.FullName)) return "Enter the faculty member's name.";
        if (!Validate.OptionalEmail(request.Email)) return "Enter a valid email address, or leave it blank.";
        if (!Enum.IsDefined(request.PayType)) return "Choose how this faculty member is paid.";
        if (request.PayType == FacultyPayType.Package && !Validate.AmountOrZero(request.AgreedPayout)) return "Enter a valid package amount.";
        if (request.PayType == FacultyPayType.Hourly && !Validate.Amount(request.HourlyRate)) return "Enter the hourly rate.";
        return null;
    }

    private static void Apply(Faculty faculty, FacultyRequest request)
    {
        faculty.FullName = request.FullName!.Trim();
        faculty.Email = Validate.Text(request.Email, 240).ToLowerInvariant();
        faculty.Phone = Validate.Text(request.Phone, 32);
        faculty.Specialization = Validate.Text(request.Specialization, 150);
        faculty.PayType = request.PayType;
        // The figure that does not apply is kept, so switching back restores it rather than losing it.
        if (request.PayType == FacultyPayType.Package) faculty.AgreedPayout = request.AgreedPayout;
        else faculty.HourlyRate = request.HourlyRate;
        faculty.Active = request.Active;
    }

    private static async Task<IResult> Create(FacultyRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (ValidateFaculty(request) is { } error) return ApiResults.Bad(error);
        var faculty = new Faculty();
        Apply(faculty, request);
        db.Faculty.Add(faculty);
        if (request.SubjectIds is { } subjectIds && await SubjectLinks.SetFacultySubjectsAsync(db, faculty.Id, subjectIds, ct) is { } linkError)
            return ApiResults.Bad(linkError);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/faculty/{faculty.Id}", new { faculty.Id });
    }

    private static async Task<IResult> Update(Guid id, FacultyRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (ValidateFaculty(request) is { } error) return ApiResults.Bad(error);

        return await db.InTransactionAsync<IResult>(async () =>
        {
            if (!await db.LockFacultyAsync(id, ct)) return Results.NotFound();
            var faculty = await db.Faculty.FirstAsync(f => f.Id == id, ct);
            var paid = await PaidToAsync(db, id, ct);

            Apply(faculty, request);
            var earned = await EarnedAsync(db, faculty, ct);
            if (earned < paid)
            {
                return ApiResults.Bad(faculty.PayType == FacultyPayType.Package
                    ? $"The package cannot be lower than the {paid:0.00} already paid."
                    : $"Logged sessions come to {earned:0.00}, less than the {paid:0.00} already paid. Keep the package, or log the remaining hours first.");
            }
            if (request.SubjectIds is { } subjectIds && await SubjectLinks.SetFacultySubjectsAsync(db, id, subjectIds, ct) is { } linkError)
                return ApiResults.Bad(linkError);

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { faculty.Id });
        }, ct);
    }

    // ---- Payouts ----

    private static async Task<IResult> ListPayouts([AsParameters] PayoutListQuery query, AcademyDbContext db, AcademicYearScope years, CancellationToken ct)
    {
        var payouts = db.FacultyPayouts.AsNoTracking().Between(await years.GetAsync(ct), p => p.PaidOn);
        if (query.FacultyId is { } facultyId) payouts = payouts.Where(p => p.FacultyId == facultyId);
        if (query.From is { } from) payouts = payouts.Where(p => p.PaidOn >= from);
        if (query.To is { } to) payouts = payouts.Where(p => p.PaidOn <= to);

        return Results.Ok(await payouts
            .OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.CreatedAtUtc)
            .Select(p => new { p.Id, p.FacultyId, FacultyName = p.Faculty.FullName, p.Amount, p.Method, p.PaidOn, p.Notes, p.Status, p.VoidReason })
            .ToPagedAsync(query, ct));
    }

    private static async Task<IResult> RecordPayout(PayoutRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (!Validate.Amount(request.Amount)) return ApiResults.Bad("Enter a positive amount with at most two decimals.");
        if (!PaymentMethods.IsValid(request.Method)) return ApiResults.Bad("Select a valid payment method.");

        return await db.InTransactionAsync<IResult>(async () =>
        {
            if (!await db.LockFacultyAsync(request.FacultyId, ct)) return ApiResults.Bad("Faculty member not found.");
            var faculty = await db.Faculty.AsNoTracking().FirstAsync(f => f.Id == request.FacultyId, ct);
            var owed = await EarnedAsync(db, faculty, ct) - await PaidToAsync(db, request.FacultyId, ct);
            if (request.Amount > owed)
                return ApiResults.Bad($"Payout exceeds the outstanding amount of {Math.Max(0, owed):0.00}.");

            var payout = new FacultyPayout
            {
                FacultyId = request.FacultyId,
                Amount = request.Amount,
                Method = request.Method!,
                PaidOn = Validate.DateOrToday(request.PaidOn),
                Notes = Validate.Text(request.Notes, 1200)
            };
            db.FacultyPayouts.Add(payout);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/payouts/{payout.Id}", new { payout.Id });
        }, ct);
    }

    private static async Task<IResult> VoidPayout(Guid id, VoidRequest request, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (!Validate.Reason(request.Reason)) return ApiResults.Bad("Give a reason for voiding this entry.");
        var payout = await db.FacultyPayouts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payout is null) return Results.NotFound();
        if (payout.Status == LedgerStatus.Voided) return ApiResults.Conflict("This entry is already voided.");

        payout.Void(currentUser.UserId, request.Reason!.Trim());
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    // ---- Hourly sessions ----

    private static async Task<IResult> ListSessions([AsParameters] SessionListQuery query, AcademyDbContext db, AcademicYearScope years, CancellationToken ct)
    {
        var sessions = db.FacultySessions.AsNoTracking().Between(await years.GetAsync(ct), s => s.TaughtOn);
        if (query.FacultyId is { } facultyId) sessions = sessions.Where(s => s.FacultyId == facultyId);

        return Results.Ok(await sessions
            .OrderByDescending(s => s.TaughtOn).ThenByDescending(s => s.CreatedAtUtc)
            .Select(s => new
            {
                s.Id, s.FacultyId, FacultyName = s.Faculty.FullName, s.TaughtOn, s.Minutes, s.HourlyRate, s.Amount, s.Topic,
                s.SubjectId, SubjectName = s.Subject != null ? s.Subject.Name : null,
                s.Status, s.VoidReason
            })
            .ToPagedAsync(query, ct));
    }

    private static async Task<IResult> LogSession(SessionRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (request.Minutes is <= 0 or > FacultySession.MaxMinutes)
            return ApiResults.Bad($"Enter the time taught in minutes, between 1 and {FacultySession.MaxMinutes}.");
        var taughtOn = Validate.DateOrToday(request.TaughtOn);
        if (taughtOn > Validate.Today.AddDays(1)) return ApiResults.Bad("The date cannot be in the future.");

        return await db.InTransactionAsync<IResult>(async () =>
        {
            if (!await db.LockFacultyAsync(request.FacultyId, ct)) return ApiResults.Bad("Faculty member not found.");
            var faculty = await db.Faculty.AsNoTracking().FirstAsync(f => f.Id == request.FacultyId, ct);
            if (faculty.PayType != FacultyPayType.Hourly)
                return ApiResults.Bad($"{faculty.FullName} is paid a package. Change them to hourly pay to log teaching time.");
            if (faculty.HourlyRate <= 0) return ApiResults.Bad($"Set an hourly rate for {faculty.FullName} first.");
            if (request.SubjectId is { } subjectId
                && !await db.FacultySubjects.AnyAsync(l => l.FacultyId == faculty.Id && l.SubjectId == subjectId, ct))
                return ApiResults.Bad($"Pick one of the subjects {faculty.FullName} teaches.");

            var session = new FacultySession
            {
                FacultyId = faculty.Id,
                TaughtOn = taughtOn,
                Minutes = request.Minutes,
                HourlyRate = faculty.HourlyRate,
                Amount = FacultySession.AmountFor(request.Minutes, faculty.HourlyRate),
                SubjectId = request.SubjectId,
                Topic = Validate.Text(request.Topic, 200)
            };
            db.FacultySessions.Add(session);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/faculty-sessions/{session.Id}", new { session.Id, session.Amount });
        }, ct);
    }

    private static async Task<IResult> VoidSession(Guid id, VoidRequest request, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (!Validate.Reason(request.Reason)) return ApiResults.Bad("Give a reason for voiding this entry.");

        return await db.InTransactionAsync<IResult>(async () =>
        {
            var facultyId = await db.FacultySessions.Where(s => s.Id == id).Select(s => (Guid?)s.FacultyId).FirstOrDefaultAsync(ct);
            if (facultyId is null || !await db.LockFacultyAsync(facultyId.Value, ct)) return Results.NotFound();

            var session = await db.FacultySessions.Include(s => s.Faculty).FirstAsync(s => s.Id == id, ct);
            if (session.Status == LedgerStatus.Voided) return ApiResults.Conflict("This entry is already voided.");

            if (session.Faculty.PayType == FacultyPayType.Hourly)
            {
                var earned = await SessionEarningsAsync(db, session.FacultyId, ct);
                var paid = await PaidToAsync(db, session.FacultyId, ct);
                if (earned - session.Amount < paid)
                    return ApiResults.Bad($"{session.Faculty.FullName} has already been paid for this time. Void the payout first.");
            }

            session.Void(currentUser.UserId, request.Reason!.Trim());
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { success = true });
        }, ct);
    }
}
