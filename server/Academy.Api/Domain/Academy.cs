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

/// <summary>A student's application from the public admission form, waiting for the academy to approve or reject it.</summary>
public sealed class AdmissionApplication : TenantEntity
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public string GuardianName { get; set; } = "";
    public string Address { get; set; } = "";
    public string Message { get; set; } = "";
    public AdmissionStatus Status { get; set; } = AdmissionStatus.Pending;
    /// <summary>The student created when the application was approved.</summary>
    public Guid? StudentId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    /// <summary>Why it was rejected; shown to staff only.</summary>
    public string ReviewNote { get; set; } = "";
    public string? IpAddress { get; set; }

    /// <summary>What the student record keeps of the fields the student table has no columns for.</summary>
    public string RemarksForStudent()
    {
        var parts = new List<string> { "Applied online" };
        if (GuardianName.Length > 0) parts.Add($"Guardian: {GuardianName}");
        if (Address.Length > 0) parts.Add($"Address: {Address}");
        if (Message.Length > 0) parts.Add($"Note: {Message}");
        return string.Join(". ", parts);
    }
}

public enum AdmissionStatus
{
    Pending,
    Approved,
    Rejected
}

public enum FacultyPayType
{
    /// <summary>A fixed amount agreed for the engagement (annual package or contract).</summary>
    Package,
    /// <summary>Paid for time taught: each logged session earns minutes × hourly rate.</summary>
    Hourly
}

public sealed class Faculty : TenantEntity
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Specialization { get; set; } = "";
    public FacultyPayType PayType { get; set; } = FacultyPayType.Package;
    /// <summary>The package amount. Used when <see cref="PayType"/> is Package.</summary>
    public decimal AgreedPayout { get; set; }
    /// <summary>Rate for one hour of teaching. Used when <see cref="PayType"/> is Hourly; copied onto each session.</summary>
    public decimal HourlyRate { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// Time an hourly faculty member taught. Time is kept in whole minutes, as academies record it ("85 min").
/// The rate is copied from the faculty member, so a later rate change does not re-price past sessions.
/// A ledger row: mistakes are voided, not edited.
/// </summary>
public sealed class FacultySession : LedgerEntry
{
    public const int MaxMinutes = 24 * 60;

    public Guid FacultyId { get; set; }
    public Faculty Faculty { get; set; } = null!;
    public DateOnly TaughtOn { get; set; }
    public int Minutes { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal Amount { get; set; }
    public string Topic { get; set; } = "";

    /// <summary>Pay for a session, to the paisa.</summary>
    public static decimal AmountFor(int minutes, decimal hourlyRate) =>
        decimal.Round(minutes * hourlyRate / 60m, 2, MidpointRounding.AwayFromZero);
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
