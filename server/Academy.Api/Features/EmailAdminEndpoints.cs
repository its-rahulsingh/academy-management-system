using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Email;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Academy.Api.Features;

/// <summary>Lets the platform owner see whether email is working and send a test message.</summary>
public static class EmailAdminEndpoints
{
    public sealed record TestEmailRequest(string? To);

    public static void MapEmailAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/email").RequireAuthorization(Policies.Owner);
        group.MapGet("", Status);
        group.MapPost("/test", SendTest);
    }

    private static async Task<IResult> Status(AcademyDbContext db, IOptions<EmailOptions> options, CancellationToken ct)
    {
        var settings = options.Value;
        var counts = await db.EmailMessages
            .GroupBy(m => m.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var failures = await db.EmailMessages.AsNoTracking()
            .Where(m => m.LastError != null)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(5)
            .Select(m => new { m.ToAddress, m.Subject, m.Category, m.Status, m.Attempts, m.LastError, m.CreatedAtUtc })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            configured = settings.IsConfigured,
            host = settings.IsConfigured ? $"{settings.Host}:{settings.Port}" : null,
            fromAddress = settings.IsConfigured ? settings.FromAddress : null,
            pending = counts.Where(c => c.Key == EmailStatus.Pending).Sum(c => c.Count),
            sent = counts.Where(c => c.Key == EmailStatus.Sent).Sum(c => c.Count),
            failed = counts.Where(c => c.Key == EmailStatus.Failed).Sum(c => c.Count),
            recentProblems = failures
        });
    }

    /// <summary>Sends straight away rather than through the queue, so the owner sees the mail server's answer immediately.</summary>
    private static async Task<IResult> SendTest(
        TestEmailRequest request, IEmailTransport transport, IOptions<EmailOptions> options, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!Validate.Email(request.To)) return ApiResults.Bad("Enter a valid email address to send the test to.");
        if (!options.Value.IsConfigured)
            return ApiResults.Bad("Email is not configured yet. Set Email__Host, Email__Port, Email__Username, Email__Password and Email__FromAddress, then restart the server.");

        var message = new EmailMessage
        {
            ToAddress = request.To!.Trim(),
            Subject = "AcademyFlow test email",
            Body = "This is a test message from AcademyFlow. If you can read it, outgoing email is working.\n",
            Category = "Test"
        };

        try
        {
            await transport.SendAsync(message, ct);
            return Results.Ok(new { sent = true });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggers.CreateLogger(typeof(EmailAdminEndpoints)).LogWarning(ex, "Test email to {To} failed", message.ToAddress);
            return ApiResults.Error(StatusCodes.Status502BadGateway, $"The mail server refused the message: {ex.Message}");
        }
    }
}
