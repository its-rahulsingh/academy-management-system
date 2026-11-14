using Academy.Api.Data;
using Academy.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>A student with the figures that make up their fee position, computed in SQL.</summary>
public sealed class StudentBalance
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = "";
    public string Email { get; init; } = "";
    public string Phone { get; init; } = "";
    public Guid CourseId { get; init; }
    public string CourseName { get; init; } = "";
    public decimal FeeAmount { get; init; }
    public DateOnly AdmissionDate { get; init; }
    public string Remarks { get; init; } = "";
    public bool Active { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public decimal Discounts { get; init; }
    public decimal Surcharges { get; init; }
    public decimal Received { get; init; }
    public decimal Refunded { get; init; }

    /// <summary>What the student owes in total after discounts and surcharges.</summary>
    public decimal Payable => Math.Max(0, FeeAmount - Discounts + Surcharges);
    /// <summary>Money kept: received minus refunded.</summary>
    public decimal Paid => Received - Refunded;
    public decimal Pending => Math.Max(0, Payable - Paid);
    public string Status => Paid >= Payable ? "Paid" : Paid > 0 ? "Partial" : "Pending";
}

public static class StudentLedger
{
    /// <summary>
    /// The one definition of a student's balance. Every screen, check and report uses this,
    /// so "pending" can never mean two different things. Voided entries are excluded.
    /// </summary>
    public static IQueryable<StudentBalance> StudentBalances(this AcademyDbContext db) =>
        db.Students.AsNoTracking().Select(s => new StudentBalance
        {
            Id = s.Id,
            FullName = s.FullName,
            Email = s.Email,
            Phone = s.Phone,
            CourseId = s.CourseId,
            CourseName = s.Course.Name,
            FeeAmount = s.FeeAmount,
            AdmissionDate = s.AdmissionDate,
            Remarks = s.Remarks,
            Active = s.Active,
            CreatedAtUtc = s.CreatedAtUtc,
            Discounts = db.FeeAdjustments
                .Where(a => a.StudentId == s.Id && a.Status == LedgerStatus.Posted && a.Kind == AdjustmentKind.Discount)
                .Sum(a => (decimal?)a.Amount) ?? 0,
            Surcharges = db.FeeAdjustments
                .Where(a => a.StudentId == s.Id && a.Status == LedgerStatus.Posted && a.Kind == AdjustmentKind.Surcharge)
                .Sum(a => (decimal?)a.Amount) ?? 0,
            Received = db.StudentPayments
                .Where(p => p.StudentId == s.Id && p.Status == LedgerStatus.Posted && p.Kind == PaymentKind.Payment)
                .Sum(p => (decimal?)p.Amount) ?? 0,
            Refunded = db.StudentPayments
                .Where(p => p.StudentId == s.Id && p.Status == LedgerStatus.Posted && p.Kind == PaymentKind.Refund)
                .Sum(p => (decimal?)p.Amount) ?? 0
        });

    public static IQueryable<StudentBalance> WithStatus(this IQueryable<StudentBalance> rows, string? status) => status switch
    {
        "Paid" => rows.Where(r => r.Received - r.Refunded >= r.FeeAmount - r.Discounts + r.Surcharges),
        "Partial" => rows.Where(r => r.Received - r.Refunded > 0 && r.Received - r.Refunded < r.FeeAmount - r.Discounts + r.Surcharges),
        "Pending" => rows.Where(r => r.Received - r.Refunded <= 0 && r.FeeAmount - r.Discounts + r.Surcharges > 0),
        "Due" => rows.Where(r => r.Received - r.Refunded < r.FeeAmount - r.Discounts + r.Surcharges),
        _ => rows
    };

    /// <summary>
    /// Row-locks the student for the rest of the transaction. Two cashiers recording against the same
    /// student are serialised, so the balance check and the insert cannot interleave into an overpayment.
    /// Returns false if the student does not exist in this academy.
    /// </summary>
    public static async Task<bool> LockStudentAsync(this AcademyDbContext db, Guid studentId, CancellationToken ct)
    {
        var tenantId = db.CurrentTenantId;
        var locked = await db.Database
            .SqlQuery<Guid>($"""select id as "Value" from students where id = {studentId} and tenant_id = {tenantId} for update""")
            .ToListAsync(ct);
        return locked.Count == 1;
    }

    public static async Task<bool> LockFacultyAsync(this AcademyDbContext db, Guid facultyId, CancellationToken ct)
    {
        var tenantId = db.CurrentTenantId;
        var locked = await db.Database
            .SqlQuery<Guid>($"""select id as "Value" from faculty where id = {facultyId} and tenant_id = {tenantId} for update""")
            .ToListAsync(ct);
        return locked.Count == 1;
    }

    /// <summary>Takes the next number from a per-academy sequence in a single atomic statement.</summary>
    public static async Task<long> NextCounterAsync(this AcademyDbContext db, string name, CancellationToken ct)
    {
        var tenantId = db.CurrentTenantId ?? throw new InvalidOperationException("A tenant is required.");
        var values = await db.Database
            .SqlQuery<long>($"""
                insert into tenant_counters (tenant_id, name, next_value) values ({tenantId}, {name}, 2)
                on conflict (tenant_id, name) do update set next_value = tenant_counters.next_value + 1
                returning next_value - 1 as "Value"
                """)
            .ToListAsync(ct);
        return values[0];
    }

    public static string ReceiptLabel(long number) => $"RCP-{number:D6}";
}
