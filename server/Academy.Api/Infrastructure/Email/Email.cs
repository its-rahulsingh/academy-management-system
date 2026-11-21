using Academy.Api.Data;
using Academy.Api.Domain;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Academy.Api.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string Section = "Email";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "AcademyFlow";
    /// <summary>
    /// Brevo's API key. When set, mail goes through Brevo's web API over HTTPS instead of SMTP, which also works on hosts
    /// that block outgoing mail ports (such as Render's free plan). Host/Username/Password are then not needed.
    /// </summary>
    public string BrevoApiKey { get; set; } = "";

    public bool UsesBrevo => !string.IsNullOrWhiteSpace(BrevoApiKey);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(FromAddress) && (UsesBrevo || !string.IsNullOrWhiteSpace(Host));
}

/// <summary>
/// Who a message appears to come from. Mail is always sent from the platform's own verified address, but an academy's
/// message carries the academy's name, and replies go to the academy's own inbox.
/// </summary>
public sealed record EmailSender(string FromName, string? ReplyTo)
{
    public const int MaxNameLength = 80;

    /// <summary>The sender for each academy among the given ids; platform mail (no academy) uses the platform name.</summary>
    public static async Task<Dictionary<Guid, EmailSender>> ForTenantsAsync(AcademyDbContext db, IEnumerable<Guid> tenantIds, CancellationToken ct)
    {
        var ids = tenantIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var rows = await db.Tenants.AsNoTracking().Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.EmailSenderName, t.ReplyToEmail })
            .ToListAsync(ct);
        return rows.ToDictionary(t => t.Id, t => new EmailSender(
            Clean(string.IsNullOrWhiteSpace(t.EmailSenderName) ? t.Name : t.EmailSenderName),
            string.IsNullOrWhiteSpace(t.ReplyToEmail) ? null : t.ReplyToEmail));
    }

    /// <summary>Display names go into a mail header: no line breaks, no quotes, kept short.</summary>
    public static string Clean(string name)
    {
        var text = new string(name.Where(c => !char.IsControl(c) && c != '"' && c != '<' && c != '>').ToArray()).Trim();
        return text.Length > MaxNameLength ? text[..MaxNameLength] : text;
    }
}

public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>Public address of the site, used to build links in emails. Example: https://app.example.com</summary>
    public string PublicBaseUrl { get; set; } = "";
}

/// <summary>Queues an email as part of the caller's unit of work. Nothing is sent until the caller saves.</summary>
public sealed class EmailQueue(AcademyDbContext db)
{
    public void Enqueue(Guid? tenantId, string to, string subject, string body, string category) =>
        db.EmailMessages.Add(new EmailMessage
        {
            TenantId = tenantId,
            ToAddress = to.Trim(),
            Subject = subject,
            Body = body,
            Category = category
        });
}

public interface IEmailTransport
{
    /// <summary>False for the stand-in used before SMTP is set up: it only writes the message to the log.</summary>
    bool IsConfigured { get; }

    Task SendAsync(EmailMessage message, EmailSender? sender, CancellationToken ct);
}

public sealed class SmtpEmailTransport(IOptions<EmailOptions> options) : IEmailTransport
{
    public bool IsConfigured => true;

    public async Task SendAsync(EmailMessage message, EmailSender? sender, CancellationToken ct)
    {
        var settings = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(sender?.FromName ?? settings.FromName, settings.FromAddress));
        if (sender?.ReplyTo is { } replyTo) mime.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        mime.To.Add(MailboxAddress.Parse(message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, SecureSocketOptions.Auto, ct);
        if (!string.IsNullOrEmpty(settings.Username))
            await client.AuthenticateAsync(settings.Username, settings.Password, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
    }
}

/// <summary>Sends through Brevo's transactional email API over HTTPS (https://developers.brevo.com).</summary>
public sealed class BrevoEmailTransport(HttpClient http, IOptions<EmailOptions> options) : IEmailTransport
{
    public const string Endpoint = "https://api.brevo.com/v3/smtp/email";

    public bool IsConfigured => true;

    public async Task SendAsync(EmailMessage message, EmailSender? sender, CancellationToken ct)
    {
        var settings = options.Value;
        var payload = new Dictionary<string, object?>
        {
            ["sender"] = new { name = sender?.FromName ?? settings.FromName, email = settings.FromAddress },
            ["to"] = new[] { new { email = message.ToAddress } },
            ["subject"] = message.Subject,
            ["textContent"] = message.Body
        };
        if (sender?.ReplyTo is { } replyTo) payload["replyTo"] = new { email = replyTo, name = sender.FromName };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(payload) };
        request.Headers.Add("api-key", settings.BrevoApiKey);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return;

        // Brevo explains refusals in JSON, e.g. {"code":"unauthorized","message":"Key not found"}.
        var body = await response.Content.ReadAsStringAsync(ct);
        string? reason = null;
        try { reason = JsonDocument.Parse(body).RootElement.GetProperty("message").GetString(); } catch (Exception) { /* not JSON */ }
        throw new InvalidOperationException($"Brevo refused the message ({(int)response.StatusCode}): {reason ?? body}");
    }
}

/// <summary>Stand-in used until email is configured: the message is written to the server log instead of sent.</summary>
public sealed class LogEmailTransport(ILogger<LogEmailTransport> logger) : IEmailTransport
{
    public bool IsConfigured => false;

    public Task SendAsync(EmailMessage message, EmailSender? sender, CancellationToken ct)
    {
        logger.LogWarning("Email not configured. Would send [{Category}] as {From} to {To}: {Subject}\n{Body}",
            message.Category, sender?.FromName, message.ToAddress, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}

/// <summary>Delivers queued email with exponential back-off. Safe to run on several servers at once.</summary>
public sealed class EmailDispatchService(IServiceScopeFactory scopes, ILogger<EmailDispatchService> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private const int MaxAttempts = 5;
    public const string NotConfiguredError = "Not delivered: email is not configured, so the message was only written to the server log.";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await DispatchBatchAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Email dispatch failed");
            }
        }
    }

    public static async Task<int> DispatchBatchAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AcademyDbContext>();
        var transport = services.GetRequiredService<IEmailTransport>();

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // SKIP LOCKED lets several servers drain the queue without sending the same message twice.
            var batch = await db.EmailMessages
                .FromSql($"""
                    select * from email_outbox
                    where status = 'Pending' and next_attempt_at_utc <= now()
                    order by next_attempt_at_utc
                    limit {BatchSize}
                    for update skip locked
                    """)
                .ToListAsync(ct);
            var senders = await EmailSender.ForTenantsAsync(db, batch.Where(m => m.TenantId != null).Select(m => m.TenantId!.Value), ct);

            foreach (var message in batch)
            {
                message.Attempts++;
                var sender = message.TenantId is { } tenantId && senders.TryGetValue(tenantId, out var s) ? s : null;
                if (!transport.IsConfigured)
                {
                    // Write it to the log so a developer can still read reset links, but never report it as sent.
                    await transport.SendAsync(message, sender, ct);
                    message.Status = EmailStatus.Failed;
                    message.LastError = NotConfiguredError;
                    continue;
                }

                try
                {
                    await transport.SendAsync(message, sender, ct);
                    message.Status = EmailStatus.Sent;
                    message.SentAtUtc = DateTime.UtcNow;
                    message.LastError = null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                    message.Status = message.Attempts >= MaxAttempts ? EmailStatus.Failed : EmailStatus.Pending;
                    message.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(Math.Pow(2, message.Attempts));
                }
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return batch.Count;
        });
    }
}

public static class EmailTemplates
{
    public static (string Subject, string Body) PasswordReset(string name, string link) => (
        "Reset your AcademyFlow password",
        $"Hello {name},\n\nUse the link below to choose a new password. It works once and expires in 60 minutes.\n\n{link}\n\nIf you did not ask for this, you can ignore this email.\n");

    public static (string Subject, string Body) Welcome(string name, string academy, string code, string email, string link) => (
        $"Your AcademyFlow account for {academy}",
        $"Hello {name},\n\nAn account has been created for you at {academy}.\n\nSign in at: {link}\nAcademy code: {code}\nEmail: {email}\n\nYour administrator will share your initial password privately. You will be asked to change it when you first sign in.\n");

    public static (string Subject, string Body) FeeReminder(string student, string academy, string course, string pending) => (
        $"Fee reminder from {academy}",
        $"Dear {student},\n\nThis is a reminder that {pending} is outstanding towards your fee for {course} at {academy}.\n\nPlease arrange payment at the earliest. If you have already paid, kindly ignore this message.\n\nRegards,\n{academy}\n");

    public static (string Subject, string Body) InvoiceIssued(string academy, string number, string amount, DateOnly dueOn, DateOnly from, DateOnly to) => (
        $"AcademyFlow invoice {number} for {academy}",
        $"Hello,\n\nInvoice {number} for {amount} has been raised for {academy}.\nPeriod: {from:dd MMM yyyy} to {to:dd MMM yyyy}\nDue on: {dueOn:dd MMM yyyy}\n\nSign in and open Subscription to pay it.\n");

    public static (string Subject, string Body) InvoicePaid(string academy, string number, string amount, DateOnly paidThrough) => (
        $"Payment received for invoice {number}",
        $"Hello,\n\nWe received {amount} for invoice {number}. The subscription for {academy} is now active through {paidThrough:dd MMM yyyy}.\n\nThank you.\n");
}
