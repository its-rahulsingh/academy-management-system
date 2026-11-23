using System.Text.Json;
using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Billing;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// An academy viewing and paying its own subscription. Deliberately outside the subscription gate:
/// an expired academy must still be able to reach these endpoints to pay.
/// </summary>
public static class BillingEndpoints
{
    public sealed record ConfirmPaymentRequest(string? OrderId, string? PaymentId, string? Signature);

    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/billing").RequirePermission(Permissions.BillingManage);
        group.MapGet("", Overview);
        group.MapPost("/invoices/{id:guid}/checkout", Checkout);
        group.MapPost("/invoices/{id:guid}/confirm", Confirm);

        // Called by Razorpay's servers: no cookie, no CSRF token, authenticated by an HMAC signature instead.
        app.MapPost("/api/billing/webhooks/razorpay", RazorpayWebhook).AllowAnonymous();
    }

    private static async Task<IResult> Overview(
        AcademyDbContext db, IPaymentGateway gateway, TenantAccessService access, ICurrentUser currentUser, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking()
            .Select(s => new
            {
                PlanName = s.Plan.Name, PlanCode = s.Plan.Code, s.Plan.Description, s.Plan.MaxStudents, s.Plan.MaxUsers,
                s.Plan.MaxFaculty, s.Plan.MaxReceiptEmailsPerMonth, s.Plan.CustomBranding,
                s.Status, s.Price, s.Currency, s.Interval, s.CurrentPeriodStart, s.CurrentPeriodEnd
            })
            .FirstOrDefaultAsync(ct);
        // The price list, so the academy can see what an upgrade gives. Upgrades are arranged with the platform owner.
        var plans = await db.Plans.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Interval).ThenBy(p => p.Price)
            .Select(p => new { p.Code, p.Name, p.Description, p.Price, p.Currency, p.Interval, p.MaxStudents, p.MaxUsers, p.MaxFaculty, p.MaxReceiptEmailsPerMonth, p.CustomBranding })
            .ToListAsync(ct);
        var tenantId = currentUser.TenantId!.Value;
        var monthStart = PlanLimits.MonthStartUtc();
        var invoices = await db.SubscriptionInvoices.AsNoTracking()
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(36)
            .Select(i => new { i.Id, i.InvoiceNumber, i.PlanName, i.Amount, i.Currency, i.PeriodStart, i.PeriodEnd, i.DueOn, i.Status, i.PaidAtUtc, i.PaymentMethod })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            subscription,
            invoices,
            plans,
            // The same counts the limits are checked against.
            usage = new
            {
                students = await db.Students.CountAsync(s => s.Active, ct),
                users = await db.Users.CountAsync(u => u.Active && u.Role != Roles.Faculty && u.Role != Roles.Student, ct),
                faculty = await db.Faculty.CountAsync(f => f.Active, ct),
                receiptEmails = await db.EmailMessages.CountAsync(m => m.TenantId == tenantId && m.Category == PlanLimits.ReceiptCategory
                    && m.CreatedAtUtc >= monthStart, ct)
            },
            onlinePayment = new { enabled = gateway.IsConfigured, provider = gateway.Name, keyId = gateway.PublicKey }
        });
    }

    private static async Task<IResult> Checkout(Guid id, AcademyDbContext db, IPaymentGateway gateway, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!gateway.IsConfigured)
            return ApiResults.Bad("Online payment is not set up yet. Pay by bank transfer and ask the platform administrator to record it.");

        var invoice = await db.SubscriptionInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return Results.NotFound();
        if (invoice.Status != InvoiceStatus.Open) return ApiResults.Conflict("This invoice is not awaiting payment.");

        try
        {
            invoice.ProviderOrderId = await gateway.CreateOrderAsync(invoice, ct);
        }
        catch (HttpRequestException ex)
        {
            loggers.CreateLogger(typeof(BillingEndpoints)).LogError(ex, "Payment order creation failed for invoice {Invoice}", invoice.InvoiceNumber);
            return ApiResults.Error(StatusCodes.Status502BadGateway, "The payment provider could not be reached. Try again shortly.");
        }
        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            orderId = invoice.ProviderOrderId,
            keyId = gateway.PublicKey,
            amount = (long)decimal.Round(invoice.Amount * 100m, 0),
            invoice.Currency,
            invoice.InvoiceNumber
        });
    }

    private static async Task<IResult> Confirm(
        Guid id, ConfirmPaymentRequest request, AcademyDbContext db, IPaymentGateway gateway, BillingService billing, CancellationToken ct)
    {
        var invoice = await db.SubscriptionInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return Results.NotFound();
        if (invoice.Status == InvoiceStatus.Paid) return Results.Ok(new { success = true });

        // The order id must be the one this server created for this invoice; otherwise a valid signature
        // for some other, cheaper payment could be replayed here.
        if (string.IsNullOrEmpty(request.OrderId) || request.OrderId != invoice.ProviderOrderId
            || !gateway.VerifyCheckoutSignature(request.OrderId, request.PaymentId ?? "", request.Signature ?? ""))
            return ApiResults.Bad("Payment could not be verified. If money was deducted it will be reconciled automatically.");

        await billing.ApplyPaymentAsync(id, gateway.Name, request.PaymentId, null, ct);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> RazorpayWebhook(
        HttpRequest request, AcademyDbContext db, IPaymentGateway gateway, BillingService billing, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(ct);
        if (!gateway.VerifyWebhookSignature(body, request.Headers["X-Razorpay-Signature"].ToString()))
            return Results.Unauthorized();

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var eventName = root.TryGetProperty("event", out var e) ? e.GetString() : null;
        if (eventName is not ("payment.captured" or "order.paid")) return Results.Ok();

        if (!root.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("payment", out var payment)
            || !payment.TryGetProperty("entity", out var entity))
            return Results.Ok();

        var orderId = entity.TryGetProperty("order_id", out var o) ? o.GetString() : null;
        var paymentId = entity.TryGetProperty("id", out var p) ? p.GetString() : null;
        if (string.IsNullOrEmpty(orderId)) return Results.Ok();

        var invoiceId = await db.SubscriptionInvoices.IgnoreQueryFilters()
            .Where(i => i.ProviderOrderId == orderId)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(ct);
        if (invoiceId is not null) await billing.ApplyPaymentAsync(invoiceId.Value, gateway.Name, paymentId, null, ct);
        return Results.Ok();
    }
}
