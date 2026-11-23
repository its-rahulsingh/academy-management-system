using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Billing;
using Academy.Api.Infrastructure.Email;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

public static class PaymentEndpoints
{
    public sealed record PaymentRequest(Guid StudentId, decimal Amount, string? Method, DateOnly? PaidOn, string? Notes);
    public sealed record VoidRequest(string? Reason);

    public sealed class PaymentListQuery : PageQuery
    {
        public string? Search { get; set; }
        public string? Method { get; set; }
        public PaymentKind? Kind { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public Guid? StudentId { get; set; }
    }

    public static void MapPaymentEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/payments");
        group.MapGet("", List).RequirePermission(Permissions.PaymentsRead);
        group.MapGet("/summary", Summary).RequirePermission(Permissions.PaymentsRead);
        group.MapGet("/{id:guid}/receipt", Receipt).RequirePermission(Permissions.PaymentsRead);
        group.MapPost("", (PaymentRequest r, AcademyDbContext db, EmailQueue emails, TenantAccessService access, CancellationToken ct) => Record(r, PaymentKind.Payment, db, emails, access, ct))
            .RequirePermission(Permissions.PaymentsWrite);
        group.MapPost("/refunds", (PaymentRequest r, AcademyDbContext db, EmailQueue emails, TenantAccessService access, CancellationToken ct) => Record(r, PaymentKind.Refund, db, emails, access, ct))
            .RequirePermission(Permissions.LedgerCorrect);
        group.MapPost("/{id:guid}/void", Void).RequirePermission(Permissions.LedgerCorrect);
    }

    public static IQueryable<StudentPayment> Filter(IQueryable<StudentPayment> payments, PaymentListQuery query)
    {
        if (query.StudentId is { } studentId) payments = payments.Where(p => p.StudentId == studentId);
        if (query.Kind is { } kind) payments = payments.Where(p => p.Kind == kind);
        if (PaymentMethods.IsValid(query.Method)) payments = payments.Where(p => p.Method == query.Method);
        if (query.From is { } from) payments = payments.Where(p => p.PaidOn >= from);
        if (query.To is { } to) payments = payments.Where(p => p.PaidOn <= to);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = QueryExtensions.Like(query.Search);
            payments = payments.Where(p => EF.Functions.ILike(p.Student.FullName, like) || EF.Functions.ILike(p.Notes, like));
        }
        return payments;
    }

    private static async Task<IResult> List([AsParameters] PaymentListQuery query, AcademyDbContext db, AcademicYearScope years, CancellationToken ct)
    {
        var year = await years.GetAsync(ct);
        var page = await Filter(db.StudentPayments.AsNoTracking().Between(year, p => p.PaidOn), query)
            .OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.ReceiptNumber)
            .Select(p => new
            {
                p.Id, p.StudentId, StudentName = p.Student.FullName, p.Kind, p.ReceiptNumber, p.Amount, p.Method, p.PaidOn,
                p.Notes, p.Status, p.VoidReason
            })
            .ToPagedAsync(query, ct);

        return Results.Ok(page.Map(p => new
        {
            p.Id, p.StudentId, p.StudentName, p.Kind, receipt = StudentLedger.ReceiptLabel(p.ReceiptNumber), p.Amount, p.Method,
            p.PaidOn, p.Notes, p.Status, p.VoidReason
        }));
    }

    private static async Task<IResult> Summary(AcademyDbContext db, AcademicYearScope years, CancellationToken ct)
    {
        var today = Validate.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var year = await years.GetAsync(ct);
        var posted = db.StudentPayments.Where(p => p.Status == LedgerStatus.Posted).Between(year, p => p.PaidOn);

        var received = await posted.Where(p => p.Kind == PaymentKind.Payment).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var refunded = await posted.Where(p => p.Kind == PaymentKind.Refund).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var month = await posted.Where(p => p.Kind == PaymentKind.Payment && p.PaidOn >= monthStart).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var count = await posted.CountAsync(p => p.Kind == PaymentKind.Payment, ct);

        return Results.Ok(new { received, refunded, net = received - refunded, thisMonth = month, count });
    }

    /// <summary>
    /// Emails the student their receipt (or refund, or cancellation notice) as part of the same save. Shows this payment,
    /// paid to date and balance due only. Returns false when the student has no email address on record, or when the
    /// plan's monthly allowance of emailed receipts is used up (<paramref name="allowed"/>); the receipt can still be printed.
    /// </summary>
    internal static bool QueueReceipt(
        EmailQueue emails, Guid? tenantId, StudentBalance student, StudentPayment payment, decimal paidToDate, decimal balanceDue,
        bool allowed, bool cancelled = false)
    {
        if (!allowed || string.IsNullOrWhiteSpace(student.Email)) return false;
        var kind = cancelled ? "Cancelled" : payment.Kind == PaymentKind.Refund ? "Refund" : "Payment";
        var (subject, text, html) = EmailTemplates.Receipt(
            kind, student.FullName, student.CourseName, StudentLedger.ReceiptLabel(payment.ReceiptNumber), Money.Format(payment.Amount, "INR"),
            payment.PaidOn, payment.Method, Money.Format(Math.Max(0, paidToDate), "INR"), Money.Format(Math.Max(0, balanceDue), "INR"),
            cancelled ? $"Reason: {payment.VoidReason}" : null);
        emails.Enqueue(tenantId, student.Email, subject, text, PlanLimits.ReceiptCategory, html);
        return true;
    }

    private static async Task<IResult> Record(PaymentRequest request, PaymentKind kind, AcademyDbContext db, EmailQueue emails, TenantAccessService access, CancellationToken ct)
    {
        if (!Validate.Amount(request.Amount)) return ApiResults.Bad("Enter a positive amount with at most two decimals.");
        if (!PaymentMethods.IsValid(request.Method)) return ApiResults.Bad("Select a valid payment method.");
        var paidOn = Validate.DateOrToday(request.PaidOn);
        if (paidOn > Validate.Today.AddDays(1)) return ApiResults.Bad("The date cannot be in the future.");

        return await db.InTransactionAsync<IResult>(async () =>
        {
            if (!await db.LockStudentAsync(request.StudentId, ct)) return ApiResults.Bad("Student not found.");
            var balance = await db.StudentBalances().FirstAsync(r => r.Id == request.StudentId, ct);

            if (kind == PaymentKind.Payment && request.Amount > balance.Pending)
                return ApiResults.Bad($"Payment exceeds the outstanding fee of {balance.Pending:0.00}.");
            if (kind == PaymentKind.Refund && request.Amount > balance.Paid)
                return ApiResults.Bad($"Refund exceeds the {balance.Paid:0.00} this student has paid.");

            var payment = new StudentPayment
            {
                StudentId = request.StudentId,
                Kind = kind,
                ReceiptNumber = await db.NextCounterAsync(TenantCounter.Receipt, ct),
                Amount = request.Amount,
                Method = request.Method!,
                PaidOn = paidOn,
                Notes = Validate.Text(request.Notes, 1200)
            };
            db.StudentPayments.Add(payment);
            var paidToDate = balance.Paid + (kind == PaymentKind.Payment ? request.Amount : -request.Amount);
            var allowed = await PlanLimits.ReceiptEmailAllowedAsync(db, access, db.CurrentTenantId, ct);
            var emailed = QueueReceipt(emails, db.CurrentTenantId, balance, payment, paidToDate, balance.Payable - paidToDate - balance.WrittenOff, allowed);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/payments/{payment.Id}",
                new { payment.Id, receipt = StudentLedger.ReceiptLabel(payment.ReceiptNumber), emailed });
        }, ct);
    }

    private static async Task<IResult> Void(Guid id, VoidRequest request, AcademyDbContext db, ICurrentUser currentUser, EmailQueue emails, TenantAccessService access, CancellationToken ct)
    {
        if (!Validate.Reason(request.Reason)) return ApiResults.Bad("Give a reason for voiding this entry.");

        return await db.InTransactionAsync<IResult>(async () =>
        {
            var studentId = await db.StudentPayments.Where(p => p.Id == id).Select(p => (Guid?)p.StudentId).FirstOrDefaultAsync(ct);
            if (studentId is null || !await db.LockStudentAsync(studentId.Value, ct)) return Results.NotFound();

            var payment = await db.StudentPayments.FirstAsync(p => p.Id == id, ct);
            if (payment.Status == LedgerStatus.Voided) return ApiResults.Conflict("This entry is already voided.");

            var balance = await db.StudentBalances().FirstAsync(r => r.Id == studentId, ct);
            if (payment.Kind == PaymentKind.Payment && balance.Received - payment.Amount < balance.Refunded)
                return ApiResults.Bad("Money from this payment has already been refunded. Void the refund first.");
            if (payment.Kind == PaymentKind.Refund && balance.Paid + payment.Amount > balance.Payable)
                return ApiResults.Bad("Voiding this refund would leave the student overpaid.");

            payment.Void(currentUser.UserId, request.Reason!.Trim());
            var paidToDate = balance.Paid + (payment.Kind == PaymentKind.Payment ? -payment.Amount : payment.Amount);
            var allowed = await PlanLimits.ReceiptEmailAllowedAsync(db, access, db.CurrentTenantId, ct);
            var emailed = QueueReceipt(emails, db.CurrentTenantId, balance, payment, paidToDate, balance.Payable - paidToDate - balance.WrittenOff, allowed, cancelled: true);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { success = true, emailed });
        }, ct);
    }

    private static Task<IResult> Receipt(Guid id, AcademyDbContext db, TenantAccessService access, ICurrentUser currentUser, CancellationToken ct) =>
        ReceiptAsync(id, null, db, access, currentUser, ct);

    /// <summary>A printable receipt. With <paramref name="onlyStudentId"/>, only that student's own payments are found.</summary>
    internal static async Task<IResult> ReceiptAsync(
        Guid id, Guid? onlyStudentId, AcademyDbContext db, TenantAccessService access, ICurrentUser currentUser, CancellationToken ct)
    {
        var payment = await db.StudentPayments.AsNoTracking()
            .Where(p => p.Id == id && (onlyStudentId == null || p.StudentId == onlyStudentId))
            .Select(p => new
            {
                p.Id, p.Kind, p.ReceiptNumber, p.Amount, p.Method, p.PaidOn, p.Notes, p.Status, p.VoidReason, p.StudentId,
                StudentName = p.Student.FullName, StudentPhone = p.Student.Phone,
                RecordedBy = db.Users.Where(u => u.Id == p.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);
        if (payment is null) return Results.NotFound();

        var balance = await db.StudentBalances().FirstAsync(r => r.Id == payment.StudentId, ct);
        var academy = await access.GetAsync(currentUser.TenantId!.Value, ct);

        return Results.Ok(new
        {
            academyName = academy!.TenantName,
            receipt = StudentLedger.ReceiptLabel(payment.ReceiptNumber),
            payment.Kind, payment.Amount, payment.Method, payment.PaidOn, payment.Notes, payment.Status, payment.VoidReason,
            payment.StudentName, payment.StudentPhone, payment.RecordedBy,
            courseName = balance.CourseName,
            totalPaid = balance.Paid,
            balanceDue = balance.Pending
        });
    }
}
