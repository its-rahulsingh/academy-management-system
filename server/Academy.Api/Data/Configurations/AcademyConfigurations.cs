using Academy.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Academy.Api.Data.Configurations;

// Every index on an academy table leads with tenant_id: all queries are scoped to one academy,
// so this keeps each academy's rows together and lookups fast however many academies share the table.

internal static class LedgerColumns
{
    public static void HasLedgerColumns<T>(this EntityTypeBuilder<T> e) where T : LedgerEntry
    {
        e.Property(x => x.Status).IsEnumText();
        e.Property(x => x.VoidReason).HasMaxLength(500);
    }
}

internal sealed class AcademicYearConfiguration : IEntityTypeConfiguration<AcademicYear>
{
    public void Configure(EntityTypeBuilder<AcademicYear> e)
    {
        e.ToTable("academic_years");
        e.HasCreatedAt();
        e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.StartDate }).IsUnique();
        e.Property(x => x.Name).HasMaxLength(40);
        e.ToTable(t => t.HasCheckConstraint("ck_academic_years_dates", "end_date > start_date"));
    }
}

internal sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> e)
    {
        e.ToTable("courses");
        e.HasCreatedAt();
        e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        e.Property(x => x.Name).HasMaxLength(Columns.Name);
        e.Property(x => x.Fee).IsMoney();
    }
}

internal sealed class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> e)
    {
        e.ToTable("subjects");
        e.HasCreatedAt();
        e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        e.Property(x => x.Name).HasMaxLength(Subject.MaxNameLength);
        e.Property(x => x.Code).HasMaxLength(Subject.MaxCodeLength);
    }
}

internal sealed class CourseSubjectConfiguration : IEntityTypeConfiguration<CourseSubject>
{
    public void Configure(EntityTypeBuilder<CourseSubject> e)
    {
        e.ToTable("course_subjects");
        // The key leads with the course, which is how the link is read; the second index answers "which courses teach this subject".
        e.HasKey(x => new { x.TenantId, x.CourseId, x.SubjectId });
        e.HasIndex(x => new { x.TenantId, x.SubjectId });
        e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FacultySubjectConfiguration : IEntityTypeConfiguration<FacultySubject>
{
    public void Configure(EntityTypeBuilder<FacultySubject> e)
    {
        e.ToTable("faculty_subjects");
        e.HasKey(x => new { x.TenantId, x.FacultyId, x.SubjectId });
        e.HasIndex(x => new { x.TenantId, x.SubjectId });
        e.HasOne<Faculty>().WithMany().HasForeignKey(x => x.FacultyId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> e)
    {
        e.ToTable("students");
        e.HasCreatedAt();
        e.HasIndex(x => new { x.TenantId, x.AcademicYearId, x.CreatedAtUtc });
        e.HasIndex(x => new { x.TenantId, x.CourseId });
        e.HasIndex(x => new { x.TenantId, x.FullName });
        e.Property(x => x.FullName).HasMaxLength(Columns.Name);
        e.Property(x => x.Email).HasMaxLength(Columns.Email);
        e.Property(x => x.Phone).HasMaxLength(Columns.Phone);
        e.Property(x => x.Remarks).HasMaxLength(Columns.Notes);
        e.Property(x => x.FeeAmount).IsMoney();
        e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.AcademicYear).WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        // A record is promoted once: at most one later record points back to it.
        e.HasIndex(x => x.PreviousStudentId).IsUnique().HasFilter("previous_student_id IS NOT NULL");
        e.HasOne<Student>().WithMany().HasForeignKey(x => x.PreviousStudentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FacultyConfiguration : IEntityTypeConfiguration<Faculty>
{
    public void Configure(EntityTypeBuilder<Faculty> e)
    {
        e.ToTable("faculty");
        e.HasCreatedAt();
        e.HasIndex(x => new { x.TenantId, x.FullName });
        e.Property(x => x.FullName).HasMaxLength(Columns.Name);
        e.Property(x => x.Email).HasMaxLength(Columns.Email);
        e.Property(x => x.Phone).HasMaxLength(Columns.Phone);
        e.Property(x => x.Specialization).HasMaxLength(Columns.Name);
        e.Property(x => x.AgreedPayout).IsMoney();
        e.Property(x => x.PayType).IsEnumText();
        e.Property(x => x.HourlyRate).IsMoney();
    }
}

internal sealed class FacultySessionConfiguration : IEntityTypeConfiguration<FacultySession>
{
    public void Configure(EntityTypeBuilder<FacultySession> e)
    {
        e.ToTable("faculty_sessions");
        e.HasCreatedAt();
        e.HasLedgerColumns();
        e.HasIndex(x => new { x.TenantId, x.FacultyId });
        e.HasIndex(x => new { x.TenantId, x.TaughtOn });
        e.Property(x => x.HourlyRate).IsMoney();
        e.Property(x => x.Amount).IsMoney();
        e.Property(x => x.Topic).HasMaxLength(200);
        e.Property(x => x.Review).IsEnumText();
        e.Property(x => x.ReviewNote).HasMaxLength(500);
        // The approval queue: sessions waiting for review.
        e.HasIndex(x => new { x.TenantId, x.Review });
        e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("ck_faculty_sessions_minutes", $"minutes > 0 and minutes <= {FacultySession.MaxMinutes}"));
        e.HasOne(x => x.Faculty).WithMany().HasForeignKey(x => x.FacultyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FacultyClockConfiguration : IEntityTypeConfiguration<FacultyClock>
{
    public void Configure(EntityTypeBuilder<FacultyClock> e)
    {
        e.ToTable("faculty_clocks");
        e.HasCreatedAt();
        e.Property(x => x.InMethod).IsEnumText();
        e.Property(x => x.OutMethod).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Reason).HasMaxLength(500);
        e.Ignore(x => x.NeedsEndTime);
        // A faculty member can have only one class running at a time.
        e.HasIndex(x => new { x.TenantId, x.FacultyId }).IsUnique().HasFilter("out_at_utc IS NULL").HasDatabaseName("ix_faculty_clocks_one_running");
        e.HasIndex(x => new { x.TenantId, x.OutAtUtc });
        e.HasIndex(x => x.SessionId);
        e.HasOne(x => x.Faculty).WithMany().HasForeignKey(x => x.FacultyId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<FacultySession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AdmissionApplicationConfiguration : IEntityTypeConfiguration<AdmissionApplication>
{
    public void Configure(EntityTypeBuilder<AdmissionApplication> e)
    {
        e.ToTable("admission_applications");
        e.HasCreatedAt();
        // The review queue lists one status at a time, newest first.
        e.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAtUtc });
        e.Property(x => x.FullName).HasMaxLength(Columns.Name);
        e.Property(x => x.Email).HasMaxLength(Columns.Email);
        e.Property(x => x.Phone).HasMaxLength(Columns.Phone);
        e.Property(x => x.GuardianName).HasMaxLength(Columns.Name);
        e.Property(x => x.Address).HasMaxLength(400);
        e.Property(x => x.Message).HasMaxLength(800);
        e.Property(x => x.Status).IsEnumText();
        e.Property(x => x.ReviewNote).HasMaxLength(500);
        e.Property(x => x.IpAddress).HasMaxLength(45);
        e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StudentPaymentConfiguration : IEntityTypeConfiguration<StudentPayment>
{
    public void Configure(EntityTypeBuilder<StudentPayment> e)
    {
        e.ToTable("student_payments");
        e.HasCreatedAt();
        e.HasLedgerColumns();
        e.HasIndex(x => new { x.TenantId, x.StudentId });
        e.HasIndex(x => new { x.TenantId, x.PaidOn });
        e.HasIndex(x => new { x.TenantId, x.ReceiptNumber }).IsUnique();
        e.Property(x => x.Kind).IsEnumText();
        e.Property(x => x.Amount).IsMoney();
        e.Property(x => x.Method).HasMaxLength(Columns.Method);
        e.Property(x => x.Notes).HasMaxLength(Columns.Notes);
        e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FeeAdjustmentConfiguration : IEntityTypeConfiguration<FeeAdjustment>
{
    public void Configure(EntityTypeBuilder<FeeAdjustment> e)
    {
        e.ToTable("fee_adjustments");
        e.HasCreatedAt();
        e.HasLedgerColumns();
        e.HasIndex(x => new { x.TenantId, x.StudentId });
        e.Property(x => x.Kind).IsEnumText();
        e.Property(x => x.Amount).IsMoney();
        e.Property(x => x.Reason).HasMaxLength(500);
        e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FacultyPayoutConfiguration : IEntityTypeConfiguration<FacultyPayout>
{
    public void Configure(EntityTypeBuilder<FacultyPayout> e)
    {
        e.ToTable("faculty_payouts");
        e.HasCreatedAt();
        e.HasLedgerColumns();
        e.HasIndex(x => new { x.TenantId, x.FacultyId });
        e.HasIndex(x => new { x.TenantId, x.PaidOn });
        e.Property(x => x.Amount).IsMoney();
        e.Property(x => x.Method).HasMaxLength(Columns.Method);
        e.Property(x => x.Notes).HasMaxLength(Columns.Notes);
        e.HasOne(x => x.Faculty).WithMany().HasForeignKey(x => x.FacultyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> e)
    {
        e.ToTable("expenses");
        e.HasCreatedAt();
        e.HasLedgerColumns();
        e.HasIndex(x => new { x.TenantId, x.SpentOn });
        e.HasIndex(x => new { x.TenantId, x.Category });
        e.Property(x => x.Title).HasMaxLength(180);
        e.Property(x => x.Amount).IsMoney();
        e.Property(x => x.Category).HasMaxLength(60);
        e.Property(x => x.Method).HasMaxLength(Columns.Method);
        e.Property(x => x.Notes).HasMaxLength(Columns.Notes);
    }
}

internal sealed class ExpenseProofConfiguration : IEntityTypeConfiguration<ExpenseProof>
{
    public void Configure(EntityTypeBuilder<ExpenseProof> e)
    {
        e.ToTable("expense_proofs", t => t.HasCheckConstraint("ck_expense_proofs_size", $"octet_length(data) <= {ExpenseProof.MaxBytes}"));
        e.HasCreatedAt();
        e.HasIndex(x => new { x.TenantId, x.ExpenseId });
        e.Property(x => x.FileName).HasMaxLength(200);
        e.Property(x => x.ContentType).HasMaxLength(40);
        e.HasOne(x => x.Expense).WithMany().HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AcademyTestConfiguration : IEntityTypeConfiguration<AcademyTest>
{
    public void Configure(EntityTypeBuilder<AcademyTest> e)
    {
        e.ToTable("academy_tests");
        e.HasCreatedAt();
        e.HasIndex(x => new { x.TenantId, x.TestDate });
        e.HasIndex(x => new { x.TenantId, x.CourseId });
        e.HasIndex(x => new { x.TenantId, x.SubjectId });
        e.Property(x => x.Title).HasMaxLength(180);
        e.Property(x => x.MaxMarks).HasPrecision(10, 2);
        e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.Notes).HasMaxLength(Columns.Notes);
        e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TestResultConfiguration : IEntityTypeConfiguration<TestResult>
{
    public void Configure(EntityTypeBuilder<TestResult> e)
    {
        e.ToTable("test_results");
        e.HasCreatedAt();
        e.HasIndex(x => new { x.TestId, x.StudentId }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.StudentId });
        e.Property(x => x.Marks).HasPrecision(10, 2);
        e.Property(x => x.Remarks).HasMaxLength(600);
        e.HasOne(x => x.Test).WithMany().HasForeignKey(x => x.TestId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TenantCounterConfiguration : IEntityTypeConfiguration<TenantCounter>
{
    public void Configure(EntityTypeBuilder<TenantCounter> e)
    {
        e.ToTable("tenant_counters");
        e.HasKey(x => new { x.TenantId, x.Name });
        e.Property(x => x.Name).HasMaxLength(40);
    }
}
