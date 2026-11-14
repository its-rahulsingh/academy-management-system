using Academy.Api.Data;
using Academy.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Academy.Api.Infrastructure.Web;

/// <summary>Every error the API returns has the same shape: <c>{ error, code? }</c>.</summary>
public static class ApiResults
{
    public static IResult Bad(string message) => Results.BadRequest(new { error = message });

    public static IResult Conflict(string message) => Results.Conflict(new { error = message });

    public static IResult Error(int statusCode, string message, string? code = null) =>
        Results.Json(new { error = message, code }, statusCode: statusCode);
}

public static class Validate
{
    public const decimal MaxAmount = 100_000_000m;

    public static bool Name(string? value, int max = 150) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max;

    public static bool Email(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 240 && System.Net.Mail.MailAddress.TryCreate(value.Trim(), out _);

    public static bool OptionalEmail(string? value) => string.IsNullOrWhiteSpace(value) || Email(value);

    public static bool Amount(decimal amount) => amount > 0 && amount <= MaxAmount && decimal.Round(amount, 2) == amount;

    public static bool AmountOrZero(decimal amount) => amount == 0 || Amount(amount);

    public static bool Reason(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length is >= 3 and <= 500;

    /// <summary>Trims and truncates free text to the column size.</summary>
    public static string Text(string? value, int max)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    public static DateOnly DateOrToday(DateOnly? value) => value is null || value == default(DateOnly) ? Today : value.Value;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}

/// <summary>Standard list contract. Lists that can grow without bound are always paged on the server.</summary>
public sealed record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public class PageQuery
{
    public const int MaxPageSize = 100;

    public int? Page { get; set; }
    public int? PageSize { get; set; }

    public int PageNumber => Math.Max(1, Page ?? 1);
    public int Size => Math.Clamp(PageSize ?? 25, 1, MaxPageSize);
}

public static class QueryExtensions
{
    public static async Task<Paged<T>> ToPagedAsync<T>(this IQueryable<T> query, PageQuery page, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page.PageNumber - 1) * page.Size).Take(page.Size).ToListAsync(ct);
        return new Paged<T>(items, total, page.PageNumber, page.Size);
    }

    public static Paged<TOut> Map<TIn, TOut>(this Paged<TIn> page, Func<TIn, TOut> map) =>
        new(page.Items.Select(map).ToList(), page.Total, page.Page, page.PageSize);

    /// <summary>Escapes LIKE wildcards in user input and wraps it for a contains search.</summary>
    public static string Like(string search) =>
        "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    /// <summary>
    /// Runs work in one transaction. Required because the connection uses a retrying execution strategy,
    /// which refuses transactions it cannot replay as a unit.
    /// </summary>
    public static Task<T> InTransactionAsync<T>(this AcademyDbContext db, Func<Task<T>> work, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await work();
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

/// <summary>
/// Gate for every academy endpoint: the caller must belong to an academy, and that academy must be
/// active with a subscription in good standing. Returns 402 so the client can show the billing screen.
/// </summary>
public sealed class TenantAccessFilter(ICurrentUser currentUser, TenantAccessService access) : IEndpointFilter
{
    public const string SubscriptionInactiveCode = "subscription_inactive";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (currentUser.TenantId is not { } tenantId)
            return ApiResults.Error(StatusCodes.Status403Forbidden, "This area is for academy accounts.");

        var state = await access.GetAsync(tenantId, context.HttpContext.RequestAborted);
        if (state is null || !state.Allowed)
            return ApiResults.Error(StatusCodes.Status402PaymentRequired,
                state?.Reason ?? "Academy not found.", SubscriptionInactiveCode);

        return await next(context);
    }
}
