using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

public static class ExpenseEndpoints
{
    public static readonly IReadOnlyList<string> Categories =
        ["Rent", "Utilities", "Salary", "Supplies", "Maintenance", "Marketing", "Travel", "Other"];

    public sealed record ExpenseRequest(string? Title, decimal Amount, string? Category, DateOnly? SpentOn, string? Method, string? Notes);
    public sealed record VoidRequest(string? Reason);

    public sealed class ExpenseListQuery : PageQuery
    {
        public string? Search { get; set; }
        public string? Method { get; set; }
        public string? Category { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
    }

    public static void MapExpenseEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/expenses");
        group.MapGet("", List).RequirePermission(Permissions.ExpensesRead);
        group.MapGet("/summary", Summary).RequirePermission(Permissions.ExpensesRead);
        group.MapPost("", Create).RequirePermission(Permissions.ExpensesWrite);
        group.MapPost("/{id:guid}/void", Void).RequirePermission(Permissions.LedgerCorrect);
    }

    public static IQueryable<Expense> Filter(IQueryable<Expense> expenses, ExpenseListQuery query)
    {
        if (PaymentMethods.IsValid(query.Method)) expenses = expenses.Where(e => e.Method == query.Method);
        if (!string.IsNullOrWhiteSpace(query.Category)) expenses = expenses.Where(e => e.Category == query.Category);
        if (query.From is { } from) expenses = expenses.Where(e => e.SpentOn >= from);
        if (query.To is { } to) expenses = expenses.Where(e => e.SpentOn <= to);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = QueryExtensions.Like(query.Search);
            expenses = expenses.Where(e => EF.Functions.ILike(e.Title, like) || EF.Functions.ILike(e.Notes, like));
        }
        return expenses;
    }

    private static async Task<IResult> List([AsParameters] ExpenseListQuery query, AcademyDbContext db, AcademicYearScope years, CancellationToken ct) =>
        Results.Ok(await Filter(db.Expenses.AsNoTracking().Between(await years.GetAsync(ct), e => e.SpentOn), query)
            .OrderByDescending(e => e.SpentOn).ThenByDescending(e => e.CreatedAtUtc)
            .Select(e => new { e.Id, e.Title, e.Amount, e.Category, e.SpentOn, e.Method, e.Notes, e.Status, e.VoidReason })
            .ToPagedAsync(query, ct));

    private static async Task<IResult> Summary(AcademyDbContext db, AcademicYearScope years, CancellationToken ct)
    {
        var today = Validate.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var posted = db.Expenses.Where(e => e.Status == LedgerStatus.Posted).Between(await years.GetAsync(ct), e => e.SpentOn);

        var byCategory = await posted.GroupBy(e => e.Category)
            .Select(g => new { Category = g.Key, Amount = g.Sum(e => e.Amount), Count = g.Count() })
            .ToListAsync(ct);
        var month = await posted.Where(e => e.SpentOn >= monthStart).SumAsync(e => (decimal?)e.Amount, ct) ?? 0;

        return Results.Ok(new
        {
            total = byCategory.Sum(c => c.Amount),
            thisMonth = month,
            count = byCategory.Sum(c => c.Count),
            topCategory = byCategory.OrderByDescending(c => c.Amount).FirstOrDefault()?.Category,
            categories = Categories
        });
    }

    private static async Task<IResult> Create(ExpenseRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (!Validate.Name(request.Title, 180)) return ApiResults.Bad("Enter a title for the expense.");
        if (!Validate.Amount(request.Amount)) return ApiResults.Bad("Enter a positive amount with at most two decimals.");
        if (!PaymentMethods.IsValid(request.Method)) return ApiResults.Bad("Select a valid payment method.");

        var expense = new Expense
        {
            Title = request.Title!.Trim(),
            Amount = request.Amount,
            Category = Categories.Contains(request.Category ?? "") ? request.Category! : "Other",
            SpentOn = Validate.DateOrToday(request.SpentOn),
            Method = request.Method!,
            Notes = Validate.Text(request.Notes, 1200)
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/expenses/{expense.Id}", new { expense.Id });
    }

    private static async Task<IResult> Void(Guid id, VoidRequest request, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (!Validate.Reason(request.Reason)) return ApiResults.Bad("Give a reason for voiding this entry.");
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (expense is null) return Results.NotFound();
        if (expense.Status == LedgerStatus.Voided) return ApiResults.Conflict("This entry is already voided.");

        expense.Void(currentUser.UserId, request.Reason!.Trim());
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }
}
