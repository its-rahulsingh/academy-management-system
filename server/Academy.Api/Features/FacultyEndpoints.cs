using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

public static class FacultyEndpoints
{
    public sealed record FacultyRequest(string? FullName, string? Email, string? Phone, string? Specialization, decimal AgreedPayout, bool Active = true);
    public sealed record PayoutRequest(Guid FacultyId, decimal Amount, string? Method, DateOnly? PaidOn, string? Notes);
    public sealed record VoidRequest(string? Reason);

    public sealed class PayoutListQuery : PageQuery
    {
        public Guid? FacultyId { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
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
    }

    private static IQueryable<decimal> PaidTo(AcademyDbContext db, Guid facultyId) =>
        db.FacultyPayouts.Where(p => p.FacultyId == facultyId && p.Status == LedgerStatus.Posted).Select(p => p.Amount);

    private static async Task<IResult> List(AcademyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Faculty.AsNoTracking()
            .OrderBy(f => f.FullName)
            .Select(f => new
            {
                f.Id, f.FullName, f.Email, f.Phone, f.Specialization, f.AgreedPayout, f.Active,
                Paid = db.FacultyPayouts.Where(p => p.FacultyId == f.Id && p.Status == LedgerStatus.Posted).Sum(p => (decimal?)p.Amount) ?? 0
            })
            .ToListAsync(ct));

    private static string? ValidateFaculty(FacultyRequest request)
    {
        if (!Validate.Name(request.FullName)) return "Enter the faculty member's name.";
        if (!Validate.OptionalEmail(request.Email)) return "Enter a valid email address, or leave it blank.";
        if (!Validate.AmountOrZero(request.AgreedPayout)) return "Enter a valid payout amount.";
        return null;
    }

    private static void Apply(Faculty faculty, FacultyRequest request)
    {
        faculty.FullName = request.FullName!.Trim();
        faculty.Email = Validate.Text(request.Email, 240).ToLowerInvariant();
        faculty.Phone = Validate.Text(request.Phone, 32);
        faculty.Specialization = Validate.Text(request.Specialization, 150);
        faculty.AgreedPayout = request.AgreedPayout;
        faculty.Active = request.Active;
    }

    private static async Task<IResult> Create(FacultyRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (ValidateFaculty(request) is { } error) return ApiResults.Bad(error);
        var faculty = new Faculty();
        Apply(faculty, request);
        db.Faculty.Add(faculty);
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
            var paid = await PaidTo(db, id).SumAsync(a => (decimal?)a, ct) ?? 0;
            if (request.AgreedPayout < paid)
                return ApiResults.Bad($"Agreed payout cannot be lower than the {paid:0.00} already paid.");

            Apply(faculty, request);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { faculty.Id });
        }, ct);
    }

    private static async Task<IResult> ListPayouts([AsParameters] PayoutListQuery query, AcademyDbContext db, CancellationToken ct)
    {
        var payouts = db.FacultyPayouts.AsNoTracking();
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
            var agreed = await db.Faculty.Where(f => f.Id == request.FacultyId).Select(f => f.AgreedPayout).FirstAsync(ct);
            var paid = await PaidTo(db, request.FacultyId).SumAsync(a => (decimal?)a, ct) ?? 0;
            if (request.Amount > agreed - paid)
                return ApiResults.Bad($"Payout exceeds the outstanding amount of {Math.Max(0, agreed - paid):0.00}.");

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
}
