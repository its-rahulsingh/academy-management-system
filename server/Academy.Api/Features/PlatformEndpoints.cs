using System.Text.RegularExpressions;
using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Billing;
using Academy.Api.Infrastructure.Email;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Academy.Api.Features;

/// <summary>
/// The platform owner's console. Owners have no tenant, so the tenant filter would hide everything:
/// each query here deliberately ignores it and names the academy it is acting on.
/// </summary>
public static partial class PlatformEndpoints
{
    public sealed record PlanRequest(
        string? Code, string? Name, string? Description, decimal Price, BillingInterval Interval,
        int? MaxStudents, int? MaxUsers, bool IsActive = true);

    public sealed record SuperAdminRequest(string? Name, string? Email, string? Password);

    public sealed record CreateTenantRequest(
        string? Name, string? Code, string? ContactName, string? ContactEmail, string? ContactPhone,
        Guid PlanId, int? TrialDays, SuperAdminRequest? SuperAdmin);

    public sealed record UpdateTenantRequest(string? Name, string? ContactName, string? ContactEmail, string? ContactPhone);
    public sealed record SuspendRequest(string? Reason);
    public sealed record UpdateSubscriptionRequest(Guid PlanId, DateOnly CurrentPeriodEnd, SubscriptionStatus Status, decimal? Price);
    public sealed record MarkPaidRequest(string? Method, string? Reference, string? Notes);
    public sealed record VoidRequest(string? Reason);
    public sealed record SetPasswordRequest(string? Password);

    public sealed class TenantListQuery : PageQuery
    {
        public string? Search { get; set; }
        public TenantStatus? Status { get; set; }
    }

    public sealed class InvoiceListQuery : PageQuery
    {
        public Guid? TenantId { get; set; }
        public InvoiceStatus? Status { get; set; }
    }

    public sealed class AuditListQuery : PageQuery
    {
        public Guid? TenantId { get; set; }
    }

    public static void MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform").RequireAuthorization(Policies.Owner);

        group.MapGet("/summary", Summary);

        group.MapGet("/plans", ListPlans);
        group.MapPost("/plans", CreatePlan);
        group.MapPut("/plans/{id:guid}", UpdatePlan);

        group.MapGet("/tenants", ListTenants);
        group.MapPost("/tenants", CreateTenant);
        group.MapGet("/tenants/{id:guid}", GetTenant);
        group.MapPut("/tenants/{id:guid}", UpdateTenant);
        group.MapPost("/tenants/{id:guid}/suspend", SuspendTenant);
        group.MapPost("/tenants/{id:guid}/activate", ActivateTenant);
        group.MapPost("/tenants/{id:guid}/super-admins", AddSuperAdmin);
        group.MapPut("/tenants/{id:guid}/users/{userId:guid}/password", SetUserPassword);
        group.MapPut("/tenants/{id:guid}/subscription", UpdateSubscription);
        group.MapPost("/tenants/{id:guid}/invoices", CreateInvoice);

        group.MapGet("/invoices", ListInvoices);
        group.MapPost("/invoices/{id:guid}/mark-paid", MarkInvoicePaid);
        group.MapPost("/invoices/{id:guid}/void", VoidInvoice);
        group.MapPost("/billing/run", RunBilling);

        group.MapGet("/audit", ListAudit);
    }

    // ---- Summary ----

    private static async Task<IResult> Summary(AcademyDbContext db, CancellationToken ct)
    {
        var tenants = await db.Tenants.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var subscriptions = await db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)
            .Where(s => db.Tenants.Any(t => t.Id == s.TenantId && t.Status == TenantStatus.Active))
            .Select(s => new { s.Price, s.Interval })
            .ToListAsync(ct);
        var open = await db.SubscriptionInvoices.IgnoreQueryFilters()
            .Where(i => i.Status == InvoiceStatus.Open)
            .Select(i => new { i.Amount, i.DueOn })
            .ToListAsync(ct);
        var thisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var collected = await db.SubscriptionInvoices.IgnoreQueryFilters()
            .Where(i => i.Status == InvoiceStatus.Paid && i.PaidAtUtc >= thisMonth)
            .SumAsync(i => (decimal?)i.Amount, ct) ?? 0;

        var today = BillingService.Today;
        return Results.Ok(new
        {
            totalTenants = tenants.Sum(t => t.Count),
            activeTenants = tenants.Where(t => t.Key == TenantStatus.Active).Sum(t => t.Count),
            suspendedTenants = tenants.Where(t => t.Key == TenantStatus.Suspended).Sum(t => t.Count),
            payingSubscriptions = subscriptions.Count,
            monthlyRecurringRevenue = decimal.Round(subscriptions.Sum(s => s.Price / s.Interval.Months()), 2),
            openInvoices = open.Count,
            openInvoiceAmount = open.Sum(i => i.Amount),
            overdueInvoices = open.Count(i => i.DueOn < today),
            collectedThisMonth = collected
        });
    }

    // ---- Plans ----

    private static async Task<IResult> ListPlans(AcademyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Plans.IgnoreQueryFilters().AsNoTracking()
            .OrderBy(p => p.Price)
            .Select(p => new
            {
                p.Id, p.Code, p.Name, p.Description, p.Price, p.Currency, p.Interval, p.MaxStudents, p.MaxUsers, p.IsActive,
                Subscribers = db.Subscriptions.Count(s => s.PlanId == p.Id && s.Status != SubscriptionStatus.Cancelled)
            })
            .ToListAsync(ct));

    private static string? ValidatePlan(PlanRequest request)
    {
        if (!Validate.Name(request.Name)) return "Enter a plan name.";
        if (!CodePattern().IsMatch((request.Code ?? "").Trim().ToLowerInvariant())) return CodeRule;
        if (!Validate.AmountOrZero(request.Price)) return "Enter a valid price.";
        if (request.MaxStudents is <= 0 || request.MaxUsers is <= 0) return "Limits must be positive, or blank for unlimited.";
        return null;
    }

    private static async Task<IResult> CreatePlan(PlanRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (ValidatePlan(request) is { } error) return ApiResults.Bad(error);
        var code = request.Code!.Trim().ToLowerInvariant();
        if (await db.Plans.AnyAsync(p => p.Code == code, ct)) return ApiResults.Conflict("A plan with that code already exists.");

        var plan = new Plan { Code = code };
        Apply(plan, request);
        db.Plans.Add(plan);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/platform/plans/{plan.Id}", new { plan.Id });
    }

    private static async Task<IResult> UpdatePlan(Guid id, PlanRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (ValidatePlan(request) is { } error) return ApiResults.Bad(error);
        var plan = await db.Plans.FindAsync([id], ct);
        if (plan is null) return Results.NotFound();

        // Existing subscriptions keep their own price snapshot; this changes the price for new sales only.
        Apply(plan, request);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { plan.Id });
    }

    private static void Apply(Plan plan, PlanRequest request)
    {
        plan.Name = request.Name!.Trim();
        plan.Description = Validate.Text(request.Description, 500);
        plan.Price = request.Price;
        plan.Interval = request.Interval;
        plan.MaxStudents = request.MaxStudents;
        plan.MaxUsers = request.MaxUsers;
        plan.IsActive = request.IsActive;
    }

    // ---- Tenants ----

    private const string CodeRule = "Code must be 3-40 characters: lowercase letters, digits and hyphens.";

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,38}[a-z0-9]$")]
    private static partial Regex CodePattern();

    private static async Task<IResult> ListTenants([AsParameters] TenantListQuery query, AcademyDbContext db, CancellationToken ct)
    {
        var tenants = db.Tenants.IgnoreQueryFilters().AsNoTracking();
        if (query.Status is { } status) tenants = tenants.Where(t => t.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = QueryExtensions.Like(query.Search);
            tenants = tenants.Where(t => EF.Functions.ILike(t.Name, like) || EF.Functions.ILike(t.Code, like) || EF.Functions.ILike(t.ContactEmail, like));
        }

        var page = await tenants
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new
            {
                t.Id, t.Code, t.Name, t.Status, t.ContactName, t.ContactEmail, t.CreatedAtUtc,
                Subscription = db.Subscriptions.Where(s => s.TenantId == t.Id)
                    .Select(s => new { s.Status, s.CurrentPeriodEnd, PlanName = s.Plan.Name, s.Price, s.Currency, s.Interval })
                    .FirstOrDefault(),
                Students = db.Students.Count(s => s.TenantId == t.Id),
                Users = db.Users.Count(u => u.TenantId == t.Id && u.Active)
            })
            .ToPagedAsync(query, ct);
        return Results.Ok(page);
    }

    private static async Task<IResult> CreateTenant(
        CreateTenantRequest request, AcademyDbContext db, SessionService sessions, EmailQueue email,
        IOptions<SubscriptionOptions> subscriptionOptions, IOptions<AppOptions> appOptions, HttpContext http, CancellationToken ct)
    {
        var code = (request.Code ?? "").Trim().ToLowerInvariant();
        if (!Validate.Name(request.Name)) return ApiResults.Bad("Enter the academy name.");
        if (!CodePattern().IsMatch(code)) return ApiResults.Bad(CodeRule);
        if (!Validate.OptionalEmail(request.ContactEmail)) return ApiResults.Bad("Enter a valid contact email.");
        if (request.SuperAdmin is not { } admin || !Validate.Name(admin.Name, 140) || !Validate.Email(admin.Email))
            return ApiResults.Bad("Enter the Super Admin's name and a valid email.");
        if (!PasswordRules.IsValid(admin.Password)) return ApiResults.Bad(PasswordRules.Message);
        var trialDays = request.TrialDays ?? subscriptionOptions.Value.DefaultTrialDays;
        if (trialDays is < 0 or > 365) return ApiResults.Bad("Trial must be between 0 and 365 days.");

        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == request.PlanId && p.IsActive, ct);
        if (plan is null) return ApiResults.Bad("Select an active plan.");
        if (await db.Tenants.AnyAsync(t => t.Code == code, ct)) return ApiResults.Conflict("That academy code is already taken.");

        var today = BillingService.Today;
        var tenant = new Tenant
        {
            Code = code,
            Name = request.Name!.Trim(),
            ContactName = Validate.Text(request.ContactName ?? admin.Name, 150),
            ContactEmail = Validate.Text(request.ContactEmail ?? admin.Email, 240).ToLowerInvariant(),
            ContactPhone = Validate.Text(request.ContactPhone, 32)
        };
        // With no trial the period ends today, so the billing cycle raises the first invoice straight away.
        var subscription = new Subscription
        {
            TenantId = tenant.Id,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Trialing,
            Price = plan.Price,
            Currency = plan.Currency,
            Interval = plan.Interval,
            CurrentPeriodStart = today,
            CurrentPeriodEnd = today.AddDays(Math.Max(trialDays, 1) - 1)
        };
        var user = new AppUser
        {
            TenantId = tenant.Id,
            Email = admin.Email!.Trim().ToLowerInvariant(),
            DisplayName = admin.Name!.Trim(),
            Role = Roles.SuperAdmin,
            MustChangePassword = true
        };
        sessions.SetPassword(user, admin.Password!);

        db.AddRange(tenant, subscription, user);
        var (subject, body) = EmailTemplates.Welcome(user.DisplayName, tenant.Name, tenant.Code, user.Email,
            AuthEndpoints.PublicBaseUrl(appOptions.Value, http));
        email.Enqueue(tenant.Id, user.Email, subject, body, "Welcome");

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return ApiResults.Conflict("That academy code is already taken.");
        }
        return Results.Created($"/api/platform/tenants/{tenant.Id}", new { tenant.Id, tenant.Code });
    }

    private static async Task<IResult> GetTenant(Guid id, AcademyDbContext db, CancellationToken ct)
    {
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return Results.NotFound();

        var subscription = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == id)
            .Select(s => new { s.Id, s.PlanId, PlanName = s.Plan.Name, s.Status, s.Price, s.Currency, s.Interval, s.CurrentPeriodStart, s.CurrentPeriodEnd })
            .FirstOrDefaultAsync(ct);
        var users = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == id)
            .OrderBy(u => u.Role).ThenBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.Role, u.Active, u.LastLoginAtUtc })
            .ToListAsync(ct);
        var invoices = await db.SubscriptionInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.TenantId == id)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(24)
            .Select(i => new { i.Id, i.InvoiceNumber, i.PlanName, i.Amount, i.Currency, i.PeriodStart, i.PeriodEnd, i.DueOn, i.Status, i.PaidAtUtc, i.PaymentMethod, i.Notes })
            .ToListAsync(ct);
        var usage = new
        {
            students = await db.Students.IgnoreQueryFilters().CountAsync(s => s.TenantId == id, ct),
            courses = await db.Courses.IgnoreQueryFilters().CountAsync(c => c.TenantId == id, ct),
            users = users.Count(u => u.Active)
        };

        return Results.Ok(new
        {
            tenant.Id, tenant.Code, tenant.Name, tenant.Status, tenant.ContactName, tenant.ContactEmail, tenant.ContactPhone,
            tenant.CreatedAtUtc, tenant.SuspendedAtUtc, tenant.SuspensionReason,
            subscription, users, invoices, usage
        });
    }

    private static async Task<IResult> UpdateTenant(Guid id, UpdateTenantRequest request, AcademyDbContext db, TenantAccessService access, CancellationToken ct)
    {
        if (!Validate.Name(request.Name)) return ApiResults.Bad("Enter the academy name.");
        if (!Validate.OptionalEmail(request.ContactEmail)) return ApiResults.Bad("Enter a valid contact email.");
        var tenant = await db.Tenants.FindAsync([id], ct);
        if (tenant is null) return Results.NotFound();

        tenant.Name = request.Name!.Trim();
        tenant.ContactName = Validate.Text(request.ContactName, 150);
        tenant.ContactEmail = Validate.Text(request.ContactEmail, 240).ToLowerInvariant();
        tenant.ContactPhone = Validate.Text(request.ContactPhone, 32);
        await db.SaveChangesAsync(ct);
        access.Invalidate(id);
        return Results.Ok(new { tenant.Id });
    }

    private static async Task<IResult> SuspendTenant(Guid id, SuspendRequest request, AcademyDbContext db, TenantAccessService access, CancellationToken ct)
    {
        if (!Validate.Reason(request.Reason)) return ApiResults.Bad("Give a reason for the suspension.");
        var tenant = await db.Tenants.FindAsync([id], ct);
        if (tenant is null) return Results.NotFound();

        tenant.Status = TenantStatus.Suspended;
        tenant.SuspendedAtUtc = DateTime.UtcNow;
        tenant.SuspensionReason = request.Reason!.Trim();
        await db.SaveChangesAsync(ct);
        access.Invalidate(id);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> ActivateTenant(Guid id, AcademyDbContext db, TenantAccessService access, CancellationToken ct)
    {
        var tenant = await db.Tenants.FindAsync([id], ct);
        if (tenant is null) return Results.NotFound();

        tenant.Status = TenantStatus.Active;
        tenant.SuspendedAtUtc = null;
        tenant.SuspensionReason = null;
        await db.SaveChangesAsync(ct);
        access.Invalidate(id);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> AddSuperAdmin(Guid id, SuperAdminRequest request, AcademyDbContext db, SessionService sessions, CancellationToken ct)
    {
        if (!Validate.Name(request.Name, 140) || !Validate.Email(request.Email)) return ApiResults.Bad("Enter a name and a valid email.");
        if (!PasswordRules.IsValid(request.Password)) return ApiResults.Bad(PasswordRules.Message);
        if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return Results.NotFound();

        var email = request.Email!.Trim().ToLowerInvariant();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.TenantId == id && u.Email == email, ct))
            return ApiResults.Conflict("That email already has an account in this academy.");

        var user = new AppUser { TenantId = id, Email = email, DisplayName = request.Name!.Trim(), Role = Roles.SuperAdmin, MustChangePassword = true };
        sessions.SetPassword(user, request.Password!);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/platform/tenants/{id}", new { user.Id });
    }

    private static async Task<IResult> SetUserPassword(
        Guid id, Guid userId, SetPasswordRequest request, AcademyDbContext db, SessionService sessions, CancellationToken ct)
    {
        if (!PasswordRules.IsValid(request.Password)) return ApiResults.Bad(PasswordRules.Message);
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == id, ct);
        if (user is null) return Results.NotFound();

        sessions.SetPassword(user, request.Password!);
        user.MustChangePassword = true;
        user.Active = true;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    // ---- Subscriptions and invoices ----

    private static async Task<IResult> UpdateSubscription(
        Guid id, UpdateSubscriptionRequest request, AcademyDbContext db, TenantAccessService access, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
        var plan = await db.Plans.FindAsync([request.PlanId], ct);
        if (plan is null) return ApiResults.Bad("Plan not found.");
        if (request.Price is { } custom && !Validate.AmountOrZero(custom)) return ApiResults.Bad("Enter a valid price.");

        var subscription = await db.Subscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == id, ct);
        if (subscription is null)
        {
            // An academy carried over from the single-academy version has no subscription until the owner assigns one.
            subscription = new Subscription { TenantId = id, CurrentPeriodStart = BillingService.Today };
            db.Subscriptions.Add(subscription);
        }
        if (request.CurrentPeriodEnd < subscription.CurrentPeriodStart) return ApiResults.Bad("Period end cannot be before the period start.");

        if (subscription.PlanId != plan.Id)
        {
            subscription.PlanId = plan.Id;
            subscription.Interval = plan.Interval;
            subscription.Currency = plan.Currency;
            subscription.Price = plan.Price;
        }
        if (request.Price is { } price) subscription.Price = price;
        subscription.CurrentPeriodEnd = request.CurrentPeriodEnd;
        subscription.Status = request.Status;
        subscription.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        access.Invalidate(id);
        return Results.Ok(new { subscription.Id });
    }

    private static async Task<IResult> CreateInvoice(Guid id, AcademyDbContext db, BillingService billing, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == id, ct);
        if (subscription is null) return Results.NotFound();
        if (subscription.Price <= 0) return ApiResults.Bad("This subscription is free; there is nothing to invoice.");

        var invoice = await billing.CreateRenewalInvoiceAsync(subscription, ct);
        return invoice is null
            ? ApiResults.Conflict("An unpaid invoice already exists for this academy.")
            : Results.Created($"/api/platform/invoices/{invoice.Id}", new { invoice.Id, invoice.InvoiceNumber });
    }

    private static async Task<IResult> ListInvoices([AsParameters] InvoiceListQuery query, AcademyDbContext db, CancellationToken ct)
    {
        var invoices = db.SubscriptionInvoices.IgnoreQueryFilters().AsNoTracking();
        if (query.TenantId is { } tenantId) invoices = invoices.Where(i => i.TenantId == tenantId);
        if (query.Status is { } status) invoices = invoices.Where(i => i.Status == status);

        var page = await invoices
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => new
            {
                i.Id, i.InvoiceNumber, i.TenantId,
                TenantName = db.Tenants.Where(t => t.Id == i.TenantId).Select(t => t.Name).FirstOrDefault(),
                i.PlanName, i.Amount, i.Currency, i.PeriodStart, i.PeriodEnd, i.DueOn, i.Status, i.PaidAtUtc,
                i.PaymentMethod, i.ProviderPaymentId, i.Notes
            })
            .ToPagedAsync(query, ct);
        return Results.Ok(page);
    }

    private static async Task<IResult> MarkInvoicePaid(Guid id, MarkPaidRequest request, BillingService billing, CancellationToken ct)
    {
        if (!PaymentMethods.IsValid(request.Method)) return ApiResults.Bad("Select how the payment was received.");
        var notes = string.Join(" · ", new[] { request.Reference, request.Notes }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));

        return await billing.ApplyPaymentAsync(id, request.Method!, null, Validate.Text(notes, 1200), ct)
            ? Results.Ok(new { success = true })
            : ApiResults.Conflict("Only an open invoice can be marked as paid.");
    }

    private static async Task<IResult> VoidInvoice(Guid id, VoidRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (!Validate.Reason(request.Reason)) return ApiResults.Bad("Give a reason for voiding the invoice.");
        var invoice = await db.SubscriptionInvoices.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return Results.NotFound();
        if (invoice.Status != InvoiceStatus.Open) return ApiResults.Conflict("Only an open invoice can be voided.");

        invoice.Status = InvoiceStatus.Void;
        invoice.Notes = Validate.Text($"Voided: {request.Reason!.Trim()}", 1200);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> RunBilling(BillingService billing, CancellationToken ct) =>
        Results.Ok(await billing.RunCycleAsync(ct));

    private static async Task<IResult> ListAudit([AsParameters] AuditListQuery query, AcademyDbContext db, CancellationToken ct)
    {
        var logs = db.AuditLogs.IgnoreQueryFilters().AsNoTracking();
        logs = query.TenantId is { } tenantId ? logs.Where(a => a.TenantId == tenantId) : logs.Where(a => a.TenantId == null);
        return Results.Ok(await logs.OrderByDescending(a => a.Id).ToPagedAsync(query, ct));
    }
}
