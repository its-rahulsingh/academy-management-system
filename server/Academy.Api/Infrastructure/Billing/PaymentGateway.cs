using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Academy.Api.Domain;
using Microsoft.Extensions.Options;

namespace Academy.Api.Infrastructure.Billing;

public sealed class RazorpayOptions
{
    public const string Section = "Razorpay";

    public string KeyId { get; set; } = "";
    public string KeySecret { get; set; } = "";
    public string WebhookSecret { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(KeySecret);
}

/// <summary>Online collection of subscription invoices. Abstracted so another provider can be swapped in.</summary>
public interface IPaymentGateway
{
    string Name { get; }
    bool IsConfigured { get; }
    /// <summary>Key the browser checkout needs. Safe to expose.</summary>
    string? PublicKey { get; }
    /// <summary>Creates a payment order for the invoice and returns the provider's order id.</summary>
    Task<string> CreateOrderAsync(SubscriptionInvoice invoice, CancellationToken ct);
    bool VerifyCheckoutSignature(string orderId, string paymentId, string signature);
    bool VerifyWebhookSignature(string rawBody, string signature);
}

public sealed class RazorpayGateway(HttpClient http, IOptions<RazorpayOptions> options) : IPaymentGateway
{
    private readonly RazorpayOptions _options = options.Value;

    public string Name => "Razorpay";
    public bool IsConfigured => _options.IsConfigured;
    public string? PublicKey => IsConfigured ? _options.KeyId : null;

    public async Task<string> CreateOrderAsync(SubscriptionInvoice invoice, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Razorpay is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.razorpay.com/v1/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}")));
        request.Content = JsonContent.Create(new
        {
            // Razorpay takes the amount in the smallest currency unit (paise).
            amount = (long)decimal.Round(invoice.Amount * 100m, 0),
            currency = invoice.Currency,
            receipt = invoice.InvoiceNumber,
            notes = new { invoice_id = invoice.Id.ToString() }
        });

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>(ct);
        return order?.Id ?? throw new InvalidOperationException("Razorpay returned no order id.");
    }

    public bool VerifyCheckoutSignature(string orderId, string paymentId, string signature) =>
        IsConfigured && Signatures.Matches($"{orderId}|{paymentId}", _options.KeySecret, signature);

    public bool VerifyWebhookSignature(string rawBody, string signature) =>
        !string.IsNullOrWhiteSpace(_options.WebhookSecret) && Signatures.Matches(rawBody, _options.WebhookSecret, signature);

    private sealed record OrderResponse([property: JsonPropertyName("id")] string Id);
}

public static class Signatures
{
    public static string HmacSha256Hex(string payload, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));

    public static bool Matches(string payload, string secret, string? signature) =>
        !string.IsNullOrEmpty(signature)
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(HmacSha256Hex(payload, secret)),
            Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
}
