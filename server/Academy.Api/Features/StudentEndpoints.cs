using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Billing;
using Academy.Api.Infrastructure.Email;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

public static class StudentEndpoints
{
    private const int MaxBulkReminders = 500;
    private const int MaxLookupResults = 300;

    public sealed record StudentRequest(
        string? FullName, string? Email, string? Phone, Guid CourseId, decimal FeeAmount, DateOnly? AdmissionDate, string? Remarks);
    public sealed record SetActiveRequest(bool Active);
    public sealed record AdjustmentRequest(AdjustmentKind Kind, decimal Amount, string? Reason);
    public sealed record VoidRequest(string? Reason);

    public sealed class StudentListQuery : PageQuery
    {
        public string? Search { get; set; }
        /// <summary>Paid, Partial, Pending, or Due (anything outstanding).</summary>
        public string? Status { get; set; }
        public Guid? CourseId { get; set; }
        public bool? Active { get; set; }
    }

    public static void MapStudentEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/students");
        group.MapGet("", List).RequirePermission(Permissions.StudentsRead);
        group.MapGet("/lookup", Lookup).RequirePermission(Permissions.StudentsRead);
        group.MapGet("/{id:guid}", Get).RequirePermission(Permissions.StudentsRead);
        group.MapPost("", Create).RequirePermission(Permissions.StudentsWrite);
        group.MapPut("/{id:guid}", Update).RequirePermission(Permissions.StudentsWrite);
        group.MapPut("/{id:guid}/active", SetActive).RequirePermission(Permissions.StudentsWrite);
        group.MapPost("/{id:guid}/adjustments", AddAdjustment).RequirePermission(Permissions.LedgerCorrect);
        group.MapPost("/adjustments/{adjustmentId:guid}/void", VoidAdjustment).RequirePermission(Permissions.LedgerCorrect);
        group.MapPost("/{id:guid}/reminder", SendReminder).RequirePermission(Permissions.RemindersSend);
        group.MapPost("/reminders", SendAllReminders).RequirePermission(Permissions.RemindersSend);
    }

    private static object ToDto(StudentBalance s) => new
    {
        s.Id, s.AcademicYearId, s.AcademicYearName, s.FullName, s.Email, s.Phone, s.CourseId, s.CourseName, s.FeeAmount, s.AdmissionDate, s.Remarks, s.Active,
        s.Discounts, s.Surcharges, s.Payable, s.Paid, s.Pending, s.Status
    };

    private static IQueryable<StudentBalance> Search(IQueryable<StudentBalance> rows, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return rows;
        var like = QueryExtensions.Like(search);
        return rows.Where(r => EF.Functions.ILike(r.FullName, like) || EF.Functions.ILike(r.Email, like)
                               || EF.Functions.ILike(r.Phone, like) || EF.Functions.ILike(r.CourseName, like));
    }

    private static async Task<IResult> List([AsParameters] StudentListQuery query, AcademyDbContext db, AcademicYearScope years, CancellationToken ct)
    {
        var year = await years.GetAsync(ct);
        var rows = Search(db.StudentBalances(), query.Search).WithStatus(query.Status);
        if (year is not null) rows = rows.Where(r => r.AcademicYearId == year.Id);
        if (query.CourseId is { } courseId) rows = rows.Where(r => r.CourseId == courseId);
        if (query.Active is { } active) rows = rows.Where(r => r.Active == active);

        var page = await rows.OrderByDescending(r => r.CreatedAtUtc).ToPagedAsync(query, ct);
        return Results.Ok(page.Map(ToDto));
    }

    /// <summary>
    /// Small typeahead list for pickers, so forms never download the whole student table.
    /// Searches every year by default, so dues from an earlier year can still be collected;
    /// yearOnly narrows it to the selected year (used when entering test marks).
    /// </summary>
    private static async Task<IResult> Lookup(
        string? search, Guid? courseId, bool? dueOnly, bool? yearOnly, int? limit, AcademyDbContext db, AcademicYearScope years, CancellationToken ct)
    {
        var rows = Search(db.StudentBalances().Where(r => r.Active), search);
        if (yearOnly == true && await years.GetAsync(ct) is { } year) rows = rows.Where(r => r.AcademicYearId == year.Id);
        if (courseId is { } id) rows = rows.Where(r => r.CourseId == id);
        if (dueOnly == true) rows = rows.WithStatus("Due");

        var items = await rows.OrderBy(r => r.FullName).Take(Math.Clamp(limit ?? 20, 1, MaxLookupResults)).ToListAsync(ct);
        return Results.Ok(items.Select(r => new { r.Id, r.FullName, r.CourseName, r.AcademicYearName, r.Pending, r.Paid }));
    }

    private static async Task<IResult> Get(Guid id, AcademyDbContext db, CancellationToken ct)
    {
        var student = await db.StudentBalances().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (student is null) return Results.NotFound();

        var payments = await db.StudentPayments.AsNoTracking()
            .Where(p => p.StudentId == id)
            .OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.ReceiptNumber)
            .Select(p => new { p.Id, p.Kind, p.ReceiptNumber, p.Amount, p.Method, p.PaidOn, p.Notes, p.Status, p.VoidReason })
            .ToListAsync(ct);
        var adjustments = await db.FeeAdjustments.AsNoTracking()
            .Where(a => a.StudentId == id)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new { a.Id, a.Kind, a.Amount, a.Reason, a.Status, a.VoidReason, a.CreatedAtUtc })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            student = ToDto(student),
            payments = payments.Select(p => new
            {
                p.Id, p.Kind, receipt = StudentLedger.ReceiptLabel(p.ReceiptNumber), p.Amount, p.Method, p.PaidOn, p.Notes, p.Status, p.VoidReason
            }),
            adjustments
        });
    }

    private static string? ValidateStudent(StudentRequest request)
    {
        if (!Validate.Name(request.FullName)) return "Enter the student's name.";
        if (!Validate.OptionalEmail(request.Email)) return "Enter a valid email address, or leave it blank.";
        if ((request.Phone ?? "").Trim().Length > 32) return "Phone number is too long.";
        if (!Validate.AmountOrZero(request.FeeAmount)) return "Enter a valid fee.";
        return null;
    }

    private static async Task<IResult> Create(
        StudentRequest request, AcademyDbContext db, TenantAccessService access, AcademicYearScope years, ICurrentUser currentUser, CancellationToken ct)
    {
        if (ValidateStudent(request) is { } error) return ApiResults.Bad(error);
        var year = await years.GetAsync(ct);
        if (await CannotAdmitAsync(db, access, currentUser, year, request.CourseId, ct) is { } refusal) return ApiResults.Bad(refusal);

        var student = new Student
        {
            AcademicYearId = year!.Id,
            FullName = request.FullName!.Trim(),
            Email = Validate.Text(request.Email, 240).ToLowerInvariant(),
            Phone = Validate.Text(request.Phone, 32),
            CourseId = request.CourseId,
            FeeAmount = request.FeeAmount,
            AdmissionDate = Validate.DateOrToday(request.AdmissionDate),
            Remarks = Validate.Text(request.Remarks, 1200)
        };
        db.Students.Add(student);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/students/{student.Id}", new { student.Id });
    }

    /// <summary>
    /// Rules every admission follows, whether typed in by staff or approved from the online form.
    /// Returns why the student cannot be admitted, or null when they can.
    /// </summary>
    internal static async Task<string?> CannotAdmitAsync(
        AcademyDbContext db, TenantAccessService access, ICurrentUser currentUser, YearRange? year, Guid courseId, CancellationToken ct)
    {
        if (year is null) return "Set up an academic year before admitting students.";
        if (year.IsClosed) return $"Academic year {year.Name} has ended. Switch to the current year to admit students.";
        if (!await db.Courses.AnyAsync(c => c.Id == courseId && c.Active, ct)) return "Select an active course.";

        var limits = await access.GetAsync(currentUser.TenantId!.Value, ct);
        if (limits?.MaxStudents is { } max && await db.Students.CountAsync(s => s.Active, ct) >= max)
            return $"Your plan allows {max} active students. Upgrade the plan to admit more.";
        return null;
    }

    private static async Task<IResult> Update(Guid id, StudentRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (ValidateStudent(request) is { } error) return ApiResults.Bad(error);

        return await db.InTransactionAsync<IResult>(async () =>
        {
            if (!await db.LockStudentAsync(id, ct)) return Results.NotFound();
            var student = await db.Students.FirstAsync(s => s.Id == id, ct);
            var balance = await db.StudentBalances().FirstAsync(r => r.Id == id, ct);

            var moneyChanges = request.FeeAmount != student.FeeAmount || request.CourseId != student.CourseId;
            if (moneyChanges && balance.Received > 0)
                return ApiResults.Bad("Fee and course are locked once a payment exists. Use a discount or surcharge to change what is owed.");
            if (request.FeeAmount - balance.Discounts + balance.Surcharges < 0)
                return ApiResults.Bad("Fee cannot be lower than the discounts already granted.");
            if (request.CourseId != student.CourseId)
            {
                if (!await db.Courses.AnyAsync(c => c.Id == request.CourseId && c.Active, ct)) return ApiResults.Bad("Select an active course.");
                if (await db.TestResults.AnyAsync(r => r.StudentId == id, ct))
                    return ApiResults.Bad("Course cannot change after test results have been recorded.");
            }

            student.FullName = request.FullName!.Trim();
            student.Email = Validate.Text(request.Email, 240).ToLowerInvariant();
            student.Phone = Validate.Text(request.Phone, 32);
            student.CourseId = request.CourseId;
            student.FeeAmount = request.FeeAmount;
            student.AdmissionDate = Validate.DateOrToday(request.AdmissionDate);
            student.Remarks = Validate.Text(request.Remarks, 1200);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { student.Id });
        }, ct);
    }

    private static async Task<IResult> SetActive(Guid id, SetActiveRequest request, AcademyDbContext db, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null) return Results.NotFound();
        student.Active = request.Active;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> AddAdjustment(Guid id, AdjustmentRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (!Validate.Amount(request.Amount)) return ApiResults.Bad("Enter a positive amount.");
        if (!Validate.Reason(request.Reason)) return ApiResults.Bad("Give a reason for this adjustment.");

        return await db.InTransactionAsync<IResult>(async () =>
        {
            if (!await db.LockStudentAsync(id, ct)) return Results.NotFound();
            var balance = await db.StudentBalances().FirstAsync(r => r.Id == id, ct);
            if (request.Kind == AdjustmentKind.Discount && request.Amount > balance.Pending)
                return ApiResults.Bad($"Discount cannot exceed the outstanding balance of {balance.Pending:0.00}. Refund first if the student has overpaid.");

            var adjustment = new FeeAdjustment { StudentId = id, Kind = request.Kind, Amount = request.Amount, Reason = request.Reason!.Trim() };
            db.FeeAdjustments.Add(adjustment);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/students/{id}", new { adjustment.Id });
        }, ct);
    }

    private static async Task<IResult> VoidAdjustment(
        Guid adjustmentId, VoidRequest request, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (!Validate.Reason(request.Reason)) return ApiResults.Bad("Give a reason for voiding this entry.");

        return await db.InTransactionAsync<IResult>(async () =>
        {
            var studentId = await db.FeeAdjustments.Where(a => a.Id == adjustmentId).Select(a => (Guid?)a.StudentId).FirstOrDefaultAsync(ct);
            if (studentId is null || !await db.LockStudentAsync(studentId.Value, ct)) return Results.NotFound();

            var adjustment = await db.FeeAdjustments.FirstAsync(a => a.Id == adjustmentId, ct);
            if (adjustment.Status == LedgerStatus.Voided) return ApiResults.Conflict("This entry is already voided.");

            var balance = await db.StudentBalances().FirstAsync(r => r.Id == studentId, ct);
            if (adjustment.Kind == AdjustmentKind.Surcharge && balance.Payable - adjustment.Amount < balance.Paid)
                return ApiResults.Bad("Voiding this surcharge would leave the student overpaid. Refund the excess first.");

            adjustment.Void(currentUser.UserId, request.Reason!.Trim());
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { success = true });
        }, ct);
    }

    private static async Task<IResult> SendReminder(Guid id, AcademyDbContext db, EmailQueue email, TenantAccessService access, ICurrentUser currentUser, CancellationToken ct)
    {
        var student = await db.StudentBalances().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (student is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(student.Email)) return ApiResults.Bad("This student has no email address on record.");
        if (student.Pending <= 0) return ApiResults.Bad("This student has nothing outstanding.");

        var academy = (await access.GetAsync(currentUser.TenantId!.Value, ct))!.TenantName;
        Queue(email, currentUser.TenantId.Value, academy, student);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { queued = 1 });
    }

    private static async Task<IResult> SendAllReminders(AcademyDbContext db, EmailQueue email, TenantAccessService access, AcademicYearScope years, ICurrentUser currentUser, CancellationToken ct)
    {
        var year = await years.GetAsync(ct);
        var due = await db.StudentBalances()
            .Where(r => r.Active && r.Email != "" && (year == null || r.AcademicYearId == year.Id))
            .WithStatus("Due")
            .OrderBy(r => r.FullName)
            .Take(MaxBulkReminders)
            .ToListAsync(ct);

        var academy = (await access.GetAsync(currentUser.TenantId!.Value, ct))!.TenantName;
        foreach (var student in due) Queue(email, currentUser.TenantId.Value, academy, student);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { queued = due.Count });
    }

    private static void Queue(EmailQueue email, Guid tenantId, string academy, StudentBalance student)
    {
        var (subject, body) = EmailTemplates.FeeReminder(student.FullName, academy, student.CourseName, Money.Format(student.Pending, "INR"));
        email.Enqueue(tenantId, student.Email, subject, body, "FeeReminder");
    }
}
