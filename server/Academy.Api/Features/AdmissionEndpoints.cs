using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// The online admission form. Anyone with the academy's link (or QR code) can apply without signing in;
/// nothing reaches the student list until someone at the academy approves the application.
/// </summary>
public static class AdmissionEndpoints
{
    /// <summary>A ceiling on unreviewed applications, so a flood of junk cannot grow the table without bound.</summary>
    public const int MaxPendingPerAcademy = 1000;

    public sealed record ApplicationRequest(
        string? FullName, string? Email, string? Phone, Guid CourseId, string? GuardianName, string? Address, string? Message,
        string? Website);
    public sealed record ApproveRequest(Guid CourseId, decimal FeeAmount, DateOnly? AdmissionDate);
    public sealed record RejectRequest(string? Reason);
    public sealed record SettingsRequest(bool Open);

    public sealed class ApplicationListQuery : PageQuery
    {
        /// <summary>Which tab of the review queue; the pending ones when not given.</summary>
        public AdmissionStatus? Status { get; set; }
    }

    public static void MapPublicAdmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/admissions").RequireRateLimiting(BrandingEndpoints.PublicRateLimitPolicy);
        group.MapGet("/{code}", Form);
        group.MapPost("/{code}", Apply);
    }

    public static void MapAdmissionEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/admissions").RequirePermission(Permissions.AdmissionsManage);
        group.MapGet("", List);
        group.MapGet("/settings", Settings);
        group.MapPut("/settings", SaveSettings);
        group.MapPost("/{id:guid}/approve", Approve);
        group.MapPost("/{id:guid}/reject", Reject);
    }

    // ---- Public ----

    /// <summary>Finds an academy by its code, ignoring the tenant filter: the visitor is not signed in.</summary>
    private static Task<Tenant?> FindAcademyAsync(AcademyDbContext db, string code, CancellationToken ct)
    {
        var normalized = code.Trim().ToLowerInvariant();
        return db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Code == normalized, ct);
    }

    /// <summary>An academy takes applications only while it is open for them and its subscription is in good standing.</summary>
    private static async Task<bool> AcceptsApplicationsAsync(Tenant tenant, TenantAccessService access, CancellationToken ct) =>
        tenant.AdmissionsOpen && (await access.GetAsync(tenant.Id, ct))?.Allowed == true;

    private static async Task<IResult> Form(string code, AcademyDbContext db, TenantAccessService access, CancellationToken ct)
    {
        var tenant = await FindAcademyAsync(db, code, ct);
        if (tenant is null || tenant.Status != TenantStatus.Active) return Results.NotFound();

        var open = await AcceptsApplicationsAsync(tenant, access, ct);
        var courses = open
            ? await db.Courses.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.TenantId == tenant.Id && c.Active)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name, c.Fee })
                .ToListAsync(ct)
            : [];

        return Results.Ok(new
        {
            academy = new { tenant.Name, tenant.Tagline, logoUrl = BrandingEndpoints.LogoUrl(tenant.Id, tenant.LogoVersion) },
            open = open && courses.Count > 0,
            courses
        });
    }

    private static async Task<IResult> Apply(
        string code, ApplicationRequest request, AcademyDbContext db, TenantAccessService access, HttpContext http, CancellationToken ct)
    {
        // A hidden field people never see. Bots fill every field; answer as if it worked so they learn nothing.
        if (!string.IsNullOrEmpty(request.Website)) return Results.Ok(new { success = true });

        if (!Validate.Name(request.FullName)) return ApiResults.Bad("Enter the student's full name.");
        var phone = (request.Phone ?? "").Trim();
        if (phone.Length is < 6 or > 32) return ApiResults.Bad("Enter a phone number we can reach you on.");
        if (!Validate.OptionalEmail(request.Email)) return ApiResults.Bad("Enter a valid email address, or leave it blank.");

        var tenant = await FindAcademyAsync(db, code, ct);
        if (tenant is null || tenant.Status != TenantStatus.Active) return Results.NotFound();
        if (!await AcceptsApplicationsAsync(tenant, access, ct))
            return ApiResults.Bad($"{tenant.Name} is not accepting online applications right now.");

        var tenantApplications = db.AdmissionApplications.IgnoreQueryFilters().Where(a => a.TenantId == tenant.Id);
        if (!await db.Courses.IgnoreQueryFilters().AnyAsync(c => c.Id == request.CourseId && c.TenantId == tenant.Id && c.Active, ct))
            return ApiResults.Bad("Select a course.");
        if (await tenantApplications.AnyAsync(a => a.Status == AdmissionStatus.Pending && a.Phone == phone && a.CourseId == request.CourseId, ct))
            return ApiResults.Conflict("An application with this phone number is already waiting for review for this course.");
        if (await tenantApplications.CountAsync(a => a.Status == AdmissionStatus.Pending, ct) >= MaxPendingPerAcademy)
            return ApiResults.Bad($"{tenant.Name} is not accepting online applications right now.");

        db.AdmissionApplications.Add(new AdmissionApplication
        {
            TenantId = tenant.Id,
            FullName = request.FullName!.Trim(),
            Email = Validate.Text(request.Email, 240).ToLowerInvariant(),
            Phone = phone,
            CourseId = request.CourseId,
            GuardianName = Validate.Text(request.GuardianName, 150),
            Address = Validate.Text(request.Address, 400),
            Message = Validate.Text(request.Message, 800),
            IpAddress = http.Connection.RemoteIpAddress?.ToString()
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    // ---- Academy ----

    private static async Task<IResult> List([AsParameters] ApplicationListQuery query, AcademyDbContext db, CancellationToken ct)
    {
        var counts = await db.AdmissionApplications
            .GroupBy(a => a.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var status = query.Status ?? AdmissionStatus.Pending;
        var page = await db.AdmissionApplications.AsNoTracking()
            .Where(a => a.Status == status)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new
            {
                a.Id, a.FullName, a.Email, a.Phone, a.CourseId, CourseName = a.Course.Name, CourseFee = a.Course.Fee,
                CourseActive = a.Course.Active, a.GuardianName, a.Address, a.Message, a.Status, a.StudentId,
                a.ReviewNote, a.ReviewedAtUtc, a.CreatedAtUtc
            })
            .ToPagedAsync(query, ct);

        return Results.Ok(new
        {
            counts = Enum.GetValues<AdmissionStatus>().ToDictionary(s => s.ToString(), s => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0),
            page
        });
    }

    private static async Task<IResult> Settings(AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == currentUser.TenantId, ct);
        return Results.Ok(new { tenant.Code, open = tenant.AdmissionsOpen, path = $"/apply/{tenant.Code}" });
    }

    private static async Task<IResult> SaveSettings(SettingsRequest request, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstAsync(t => t.Id == currentUser.TenantId, ct);
        tenant.AdmissionsOpen = request.Open;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { open = tenant.AdmissionsOpen });
    }

    /// <summary>Takes a row lock so two people approving the same application cannot admit the student twice.</summary>
    private static async Task<AdmissionApplication?> LockAsync(AcademyDbContext db, Guid id, CancellationToken ct)
    {
        var tenantId = db.CurrentTenantId;
        var locked = await db.Database
            .SqlQuery<Guid>($"""select id as "Value" from admission_applications where id = {id} and tenant_id = {tenantId} for update""")
            .ToListAsync(ct);
        return locked.Count == 1 ? await db.AdmissionApplications.FirstAsync(a => a.Id == id, ct) : null;
    }

    private static async Task<IResult> Approve(
        Guid id, ApproveRequest request, AcademyDbContext db, TenantAccessService access, AcademicYearScope years,
        ICurrentUser currentUser, CancellationToken ct)
    {
        if (!Validate.AmountOrZero(request.FeeAmount)) return ApiResults.Bad("Enter a valid fee.");
        var year = await years.GetAsync(ct);

        return await db.InTransactionAsync<IResult>(async () =>
        {
            var application = await LockAsync(db, id, ct);
            if (application is null) return Results.NotFound();
            if (application.Status != AdmissionStatus.Pending) return ApiResults.Conflict("This application has already been reviewed.");
            if (await StudentEndpoints.CannotAdmitAsync(db, access, currentUser, year, request.CourseId, ct) is { } refusal)
                return ApiResults.Bad(refusal);

            var student = new Student
            {
                AcademicYearId = year!.Id,
                FullName = application.FullName,
                Email = application.Email,
                Phone = application.Phone,
                CourseId = request.CourseId,
                FeeAmount = request.FeeAmount,
                AdmissionDate = Validate.DateOrToday(request.AdmissionDate),
                Remarks = Validate.Text(application.RemarksForStudent(), 1200)
            };
            db.Students.Add(student);

            application.Status = AdmissionStatus.Approved;
            application.StudentId = student.Id;
            application.ReviewedByUserId = currentUser.UserId;
            application.ReviewedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { studentId = student.Id });
        }, ct);
    }

    private static async Task<IResult> Reject(Guid id, RejectRequest request, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (!Validate.Reason(request.Reason)) return ApiResults.Bad("Give a reason for rejecting this application.");

        return await db.InTransactionAsync<IResult>(async () =>
        {
            var application = await LockAsync(db, id, ct);
            if (application is null) return Results.NotFound();
            if (application.Status != AdmissionStatus.Pending) return ApiResults.Conflict("This application has already been reviewed.");

            application.Status = AdmissionStatus.Rejected;
            application.ReviewNote = request.Reason!.Trim();
            application.ReviewedByUserId = currentUser.UserId;
            application.ReviewedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { success = true });
        }, ct);
    }
}
