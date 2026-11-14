namespace Academy.Api.Domain;

public sealed class Course : TenantEntity
{
    public string Name { get; set; } = "";
    public decimal Fee { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class Student : TenantEntity
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;
    /// <summary>Fee agreed at admission. A snapshot: later course price changes do not alter it.</summary>
    public decimal FeeAmount { get; set; }
    public DateOnly AdmissionDate { get; set; }
    public string Remarks { get; set; } = "";
    public bool Active { get; set; } = true;
}

public sealed class Faculty : TenantEntity
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Specialization { get; set; } = "";
    public decimal AgreedPayout { get; set; }
    public bool Active { get; set; } = true;
}

public enum PaymentKind
{
    Payment,
    Refund
}

/// <summary>Money received from (Payment) or returned to (Refund) a student. Amount is always positive.</summary>
public sealed class StudentPayment : LedgerEntry
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public PaymentKind Kind { get; set; } = PaymentKind.Payment;
    /// <summary>Sequential per academy; printed on the receipt.</summary>
    public long ReceiptNumber { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";
    public DateOnly PaidOn { get; set; }
    public string Notes { get; set; } = "";
}

public enum AdjustmentKind
{
    Discount,
    Surcharge
}

/// <summary>A change to what a student owes without touching the admission fee: scholarship, waiver, late fee.</summary>
public sealed class FeeAdjustment : LedgerEntry
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public AdjustmentKind Kind { get; set; } = AdjustmentKind.Discount;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class FacultyPayout : LedgerEntry
{
    public Guid FacultyId { get; set; }
    public Faculty Faculty { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateOnly PaidOn { get; set; }
    public string Method { get; set; } = "Cash";
    public string Notes { get; set; } = "";
}

public sealed class Expense : LedgerEntry
{
    public string Title { get; set; } = "";
    public decimal Amount { get; set; }
    public string Category { get; set; } = "Other";
    public DateOnly SpentOn { get; set; }
    public string Method { get; set; } = "Cash";
    public string Notes { get; set; } = "";
}

public sealed class AcademyTest : TenantEntity
{
    public string Title { get; set; } = "";
    public string Subject { get; set; } = "";
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public decimal MaxMarks { get; set; }
    public DateOnly TestDate { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class TestResult : TenantEntity
{
    public Guid TestId { get; set; }
    public AcademyTest Test { get; set; } = null!;
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public decimal Marks { get; set; }
    public string Remarks { get; set; } = "";
}

/// <summary>Per-academy sequence (for example receipt numbers) advanced atomically in SQL.</summary>
public sealed class TenantCounter : ITenantOwned
{
    public const string Receipt = "receipt";

    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public long NextValue { get; set; } = 1;
}
