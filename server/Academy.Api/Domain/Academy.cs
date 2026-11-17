namespace Academy.Api.Domain;

public sealed class Course : TenantEntity
{
    public string Name { get; set; } = "";
    public decimal Fee { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// An academy's academic year. Each academy sets its own dates, because academies start their year in
/// different months. A year whose end date has passed is closed and its dates can no longer change.
/// </summary>
public sealed class AcademicYear : TenantEntity
{
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    /// <summary>Last day of the year, inclusive.</summary>
    public DateOnly EndDate { get; set; }

    public bool IsClosedOn(DateOnly today) => EndDate < today;

    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;

    /// <summary>The default first year for a new academy: April to March, the common Indian academic and financial year.</summary>
    public static AcademicYear DefaultFor(Guid tenantId, DateOnly today)
    {
        var startYear = today.Month >= 4 ? today.Year : today.Year - 1;
        return new AcademicYear
        {
            TenantId = tenantId,
            Name = $"{startYear}-{(startYear + 1) % 100:D2}",
            StartDate = new DateOnly(startYear, 4, 1),
            EndDate = new DateOnly(startYear + 1, 3, 31)
        };
    }
}

public sealed class Student : TenantEntity
{
    /// <summary>The academic year the student was admitted into. Lists of students are shown per year.</summary>
    public Guid AcademicYearId { get; set; }
    public AcademicYear AcademicYear { get; set; } = null!;
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
