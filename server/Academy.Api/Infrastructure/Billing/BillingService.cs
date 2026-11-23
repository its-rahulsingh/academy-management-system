using System.Globalization;
using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Email;
using Academy.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Academy.Api.Infrastructure.Billing;

public static class Money
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public static string Format(decimal amount, string currency) =>
        currency == "INR" ? "₹" + amount.ToString("N2", India) : $"{currency} {amount:N2}";
}

public sealed record BillingCycleResult(int InvoicesCreated, int MarkedPastDue, int MovedToFree = 0);

/// <summary>
/// Subscription lifecycle: raise the renewal invoice ahead of period end, extend access when it is paid,
/// flag subscriptions that lapse. Works across all academies, so every query here ignores the tenant filter
/// and names the tenant explicitly.
/// </summary>
public sealed class BillingService(
    AcademyDbContext db,
    EmailQueue email,
    TenantAccessService access,
    IOptions<SubscriptionOptions> options,
    ILogger<BillingService> logger)
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static DateOnly PeriodEndFor(DateOnly start, BillingInterval interval) =>
        start.AddMonths(interval.Months()).AddDays(-1);

    /// <summary>Raises the invoice for the period after the current one. Returns null if it already exists.</summary>
    public async Task<SubscriptionInvoice?> CreateRenewalInvoiceAsync(Subscription subscription, CancellationToken ct)
    {
        var today = Today;
        // A subscription that lapsed beyond grace restarts from today, so nobody pays for days they were locked out.
        var lapsed = today > subscription.CurrentPeriodEnd.AddDays(options.Value.GraceDays);
        var start = lapsed ? today : subscription.CurrentPeriodEnd.AddDays(1);

        var exists = await db.SubscriptionInvoices.IgnoreQueryFilters().AnyAsync(i =>
            i.SubscriptionId == subscription.Id && i.Status != InvoiceStatus.Void
            && (i.PeriodStart == start || i.Status == InvoiceStatus.Open), ct);
        if (exists) return null;

        var planName = await db.Plans.Where(p => p.Id == subscription.PlanId).Select(p => p.Name).FirstAsync(ct);
        var sequence = (await db.Database
            .SqlQuery<long>($"""select nextval('subscription_invoice_number_seq') as "Value" """)
            .ToListAsync(ct))[0];

        var invoice = new SubscriptionInvoice
        {
            TenantId = subscription.TenantId,
            SubscriptionId = subscription.Id,
            InvoiceNumber = $"INV-{today.Year}-{sequence:D6}",
            PlanName = planName,
            Amount = subscription.Price,
            Currency = subscription.Currency,
            PeriodStart = start,
            PeriodEnd = PeriodEndFor(start, subscription.Interval),
            DueOn = lapsed ? today : subscription.CurrentPeriodEnd
        };
        db.SubscriptionInvoices.Add(invoice);

        var tenantName = await db.Tenants.Where(t => t.Id == subscription.TenantId).Select(t => t.Name).FirstAsync(ct);
        var (subject, body) = EmailTemplates.InvoiceIssued(tenantName, invoice.InvoiceNumber,
            Money.Format(invoice.Amount, invoice.Currency), invoice.DueOn, invoice.PeriodStart, invoice.PeriodEnd);
        foreach (var address in await SuperAdminEmailsAsync(subscription.TenantId, ct))
            email.Enqueue(subscription.TenantId, address, subject, body, "Invoice");

        await db.SaveChangesAsync(ct);
        return invoice;
    }

    /// <summary>Marks an open invoice paid and moves the subscription onto the period it covers. Idempotent.</summary>
    public async Task<bool> ApplyPaymentAsync(Guid invoiceId, string method, string? providerPaymentId, string? notes, CancellationToken ct)
    {
        var invoice = await db.SubscriptionInvoices.IgnoreQueryFilters()
            .Include(i => i.Subscription)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice is null || invoice.Status != InvoiceStatus.Open) return false;

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAtUtc = DateTime.UtcNow;
        invoice.PaymentMethod = method;
        invoice.ProviderPaymentId = providerPaymentId ?? invoice.ProviderPaymentId;
        if (!string.IsNullOrWhiteSpace(notes)) invoice.Notes = notes.Trim();

        var subscription = invoice.Subscription;
        if (invoice.PeriodEnd > subscription.CurrentPeriodEnd)
        {
            subscription.CurrentPeriodStart = invoice.PeriodStart;
            subscription.CurrentPeriodEnd = invoice.PeriodEnd;
        }
        subscription.Status = SubscriptionStatus.Active;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        var tenantName = await db.Tenants.Where(t => t.Id == invoice.TenantId).Select(t => t.Name).FirstAsync(ct);
        var (subject, body) = EmailTemplates.InvoicePaid(tenantName, invoice.InvoiceNumber,
            Money.Format(invoice.Amount, invoice.Currency), subscription.CurrentPeriodEnd);
        foreach (var address in await SuperAdminEmailsAsync(invoice.TenantId, ct))
            email.Enqueue(invoice.TenantId, address, subject, body, "Invoice");

        await db.SaveChangesAsync(ct);
        access.Invalidate(invoice.TenantId);
        return true;
    }

    /// <summary>
    /// One pass of the recurring billing job. Each subscription is handled on its own so one failure cannot block the rest.
    /// Free subscriptions (price 0) never expire, so they are left alone. A paid trial that ends unpaid, and a paid
    /// subscription still unpaid when its grace period ends, move to the Free plan instead of being locked out.
    /// </summary>
    public async Task<BillingCycleResult> RunCycleAsync(CancellationToken ct)
    {
        var today = Today;
        var invoiceFrom = today.AddDays(options.Value.InvoiceLeadDays);
        var due = await db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.Status != SubscriptionStatus.Cancelled && s.Price > 0 && s.CurrentPeriodEnd <= invoiceFrom)
            .Where(s => db.Tenants.Any(t => t.Id == s.TenantId && t.Status == TenantStatus.Active))
            .ToListAsync(ct);

        int created = 0, pastDue = 0, movedToFree = 0;
        foreach (var subscription in due)
        {
            try
            {
                if (subscription.Status == SubscriptionStatus.Trialing && today > subscription.CurrentPeriodEnd)
                {
                    if (await MoveToFreeAsync(subscription, "Your free trial has ended", ct)) { movedToFree++; continue; }
                }
                else if (subscription.Status == SubscriptionStatus.PastDue && today > subscription.CurrentPeriodEnd.AddDays(options.Value.GraceDays))
                {
                    if (await MoveToFreeAsync(subscription, "The invoice for your plan was not paid in time", ct)) { movedToFree++; continue; }
                }

                if (await CreateRenewalInvoiceAsync(subscription, ct) is not null) created++;

                if (today > subscription.CurrentPeriodEnd && subscription.Status is SubscriptionStatus.Trialing or SubscriptionStatus.Active)
                {
                    subscription.Status = SubscriptionStatus.PastDue;
                    subscription.UpdatedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    access.Invalidate(subscription.TenantId);
                    pastDue++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Billing failed for subscription {SubscriptionId}", subscription.Id);
                db.ChangeTracker.Clear();
            }
        }
        return new BillingCycleResult(created, pastDue, movedToFree);
    }

    /// <summary>
    /// Puts the subscription on the Free plan, voids its unpaid invoices and tells the Super Admins.
    /// Returns false when there is no active Free plan, so the caller falls back to the paid lifecycle.
    /// Nothing is deleted: records over the Free limits stay, the academy just cannot add more.
    /// </summary>
    public async Task<bool> MoveToFreeAsync(Subscription subscription, string why, CancellationToken ct)
    {
        var free = await db.Plans.FirstOrDefaultAsync(p => p.Code == PlanCodes.Free && p.IsActive, ct);
        if (free is null || subscription.PlanId == free.Id && subscription.Price <= 0) return false;

        var today = Today;
        subscription.PlanId = free.Id;
        subscription.Price = free.Price;
        subscription.Currency = free.Currency;
        subscription.Interval = free.Interval;
        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodStart = today;
        subscription.CurrentPeriodEnd = today;
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        var open = await db.SubscriptionInvoices.IgnoreQueryFilters()
            .Where(i => i.SubscriptionId == subscription.Id && i.Status == InvoiceStatus.Open)
            .ToListAsync(ct);
        foreach (var invoice in open)
        {
            invoice.Status = InvoiceStatus.Void;
            invoice.Notes = "Moved to the Free plan.";
        }

        var tenantName = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == subscription.TenantId).Select(t => t.Name).FirstAsync(ct);
        var (subject, body) = EmailTemplates.MovedToFree(tenantName, why, free);
        foreach (var address in await SuperAdminEmailsAsync(subscription.TenantId, ct))
            email.Enqueue(subscription.TenantId, address, subject, body, "Invoice");

        await db.SaveChangesAsync(ct);
        access.Invalidate(subscription.TenantId);
        return true;
    }

    private Task<List<string>> SuperAdminEmailsAsync(Guid tenantId, CancellationToken ct) =>
        db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.Role == Roles.SuperAdmin && u.Active)
            .Select(u => u.Email)
            .ToListAsync(ct);
}

public sealed class BillingCycleService(IServiceScopeFactory scopes, ILogger<BillingCycleService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<BillingService>().RunCycleAsync(stoppingToken);
                if (result.InvoicesCreated > 0 || result.MarkedPastDue > 0 || result.MovedToFree > 0)
                    logger.LogInformation("Billing cycle: {Created} invoices raised, {PastDue} subscriptions past due, {Free} moved to Free",
                        result.InvoicesCreated, result.MarkedPastDue, result.MovedToFree);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Billing cycle failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
