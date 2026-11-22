using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

public static class ExpenseEndpoints
{
    public static readonly IReadOnlyList<string> Categories =
        ["Rent", "Utilities", "Salary", "Supplies", "Maintenance", "Marketing", "Travel", "Other"];

    public sealed record ExpenseRequest(
        string? Title, decimal Amount, string? Category, DateOnly? SpentOn, string? Method, string? Notes, List<ProofUpload>? Proofs = null);
    /// <summary>A file sent as a base64 data URL, as the browser reads it.</summary>
    public sealed record ProofUpload(string? FileName, string? DataUrl);
    public sealed record ProofSettingsRequest(decimal? ProofRequiredAbove);
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
        // Up to five files of 5 MB each, sent as base64, can exceed the server's default 30 MB request limit.
        group.MapPost("", Create).RequirePermission(Permissions.ExpensesWrite).WithMetadata(new RequestSizeLimitAttribute(48_000_000));
        group.MapPost("/{id:guid}/void", Void).RequirePermission(Permissions.LedgerCorrect);
        group.MapPut("/settings", SaveSettings).RequirePermission(Permissions.SettingsManage);

        group.MapGet("/{id:guid}/proofs", ListProofs).RequirePermission(Permissions.ExpensesRead);
        group.MapGet("/{id:guid}/proofs/{proofId:guid}", GetProof).RequirePermission(Permissions.ExpensesRead);
        group.MapPost("/{id:guid}/proofs", AddProof).RequirePermission(Permissions.ExpensesWrite);
        group.MapDelete("/{id:guid}/proofs/{proofId:guid}", RemoveProof).RequirePermission(Permissions.ExpensesWrite);
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
            .Select(e => new
            {
                e.Id, e.Title, e.Amount, e.Category, e.SpentOn, e.Method, e.Notes, e.Status, e.VoidReason,
                ProofCount = db.ExpenseProofs.Count(p => p.ExpenseId == e.Id)
            })
            .ToPagedAsync(query, ct));

    private static async Task<IResult> Summary(AcademyDbContext db, AcademicYearScope years, CancellationToken ct)
    {
        var proofRequiredAbove = await ProofLimitAsync(db, ct);
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
            categories = Categories,
            proofRequiredAbove,
            maxProofs = ExpenseProof.MaxPerExpense,
            maxProofMb = ExpenseProof.MaxBytes / (1024 * 1024)
        });
    }

    private static async Task<IResult> Create(ExpenseRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (!Validate.Name(request.Title, 180)) return ApiResults.Bad("Enter a title for the expense.");
        if (!Validate.Amount(request.Amount)) return ApiResults.Bad("Enter a positive amount with at most two decimals.");
        if (!PaymentMethods.IsValid(request.Method)) return ApiResults.Bad("Select a valid payment method.");

        var uploads = request.Proofs ?? [];
        if (uploads.Count > ExpenseProof.MaxPerExpense) return ApiResults.Bad($"Attach at most {ExpenseProof.MaxPerExpense} files.");
        var proofs = new List<ExpenseProof>();
        foreach (var upload in uploads)
        {
            if (!TryReadProof(upload, out var proof, out var error)) return ApiResults.Bad(error);
            proofs.Add(proof);
        }
        if (await ProofLimitAsync(db, ct) is { } above && request.Amount > above && proofs.Count == 0)
            return ApiResults.Bad($"Expenses above {Money(above)} need a bill or receipt attached.");

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
        foreach (var proof in proofs)
        {
            proof.Expense = expense;
            db.ExpenseProofs.Add(proof);
        }
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/expenses/{expense.Id}", new { expense.Id, proofs = proofs.Count });
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

    private static Task<decimal?> ProofLimitAsync(AcademyDbContext db, CancellationToken ct) =>
        db.Tenants.Where(t => t.Id == db.CurrentTenantId).Select(t => t.ExpenseProofRequiredAbove).FirstOrDefaultAsync(ct);

    private static string Money(decimal amount) => "₹" + amount.ToString("#,##,##0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<IResult> SaveSettings(ProofSettingsRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (request.ProofRequiredAbove is { } above && !Validate.AmountOrZero(above))
            return ApiResults.Bad("Enter an amount of zero or more, with at most two decimals.");
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == db.CurrentTenantId, ct);
        if (tenant is null) return Results.NotFound();
        tenant.ExpenseProofRequiredAbove = request.ProofRequiredAbove;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { proofRequiredAbove = tenant.ExpenseProofRequiredAbove });
    }

    private static async Task<IResult> ListProofs(Guid id, AcademyDbContext db, CancellationToken ct)
    {
        if (!await db.Expenses.AnyAsync(e => e.Id == id, ct)) return Results.NotFound();
        var users = db.Users.AsNoTracking();
        return Results.Ok(await db.ExpenseProofs.AsNoTracking().Where(p => p.ExpenseId == id).OrderBy(p => p.CreatedAtUtc)
            .Select(p => new
            {
                p.Id, p.FileName, p.ContentType, p.SizeBytes, p.CreatedAtUtc,
                AddedBy = users.Where(u => u.Id == p.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefault() ?? ""
            })
            .ToListAsync(ct));
    }

    private static async Task<IResult> GetProof(Guid id, Guid proofId, AcademyDbContext db, HttpContext http, CancellationToken ct)
    {
        var proof = await db.ExpenseProofs.AsNoTracking().Where(p => p.Id == proofId && p.ExpenseId == id)
            .Select(p => new { p.Data, p.ContentType }).FirstOrDefaultAsync(ct);
        if (proof is null) return Results.NotFound();
        // Shown in the page, never run: the type was checked from the bytes when it was uploaded.
        http.Response.Headers["Content-Disposition"] = "inline";
        http.Response.Headers["Cache-Control"] = "private, max-age=3600";
        return Results.File(proof.Data, proof.ContentType);
    }

    private static async Task<IResult> AddProof(Guid id, ProofUpload upload, AcademyDbContext db, CancellationToken ct)
    {
        var expense = await db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (expense is null) return Results.NotFound();
        if (expense.Status == LedgerStatus.Voided) return ApiResults.Conflict("This expense is voided; its proof stays as it is.");
        if (await db.ExpenseProofs.CountAsync(p => p.ExpenseId == id, ct) >= ExpenseProof.MaxPerExpense)
            return ApiResults.Conflict($"An expense can have at most {ExpenseProof.MaxPerExpense} files.");
        if (!TryReadProof(upload, out var proof, out var error)) return ApiResults.Bad(error);

        proof.ExpenseId = id;
        db.ExpenseProofs.Add(proof);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { proof.Id, proof.FileName, proof.ContentType, proof.SizeBytes });
    }

    private static async Task<IResult> RemoveProof(Guid id, Guid proofId, AcademyDbContext db, CancellationToken ct)
    {
        var expense = await db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        var proof = await db.ExpenseProofs.FirstOrDefaultAsync(p => p.Id == proofId && p.ExpenseId == id, ct);
        if (expense is null || proof is null) return Results.NotFound();
        if (expense.Status == LedgerStatus.Voided) return ApiResults.Conflict("This expense is voided; its proof stays on record.");

        if (await ProofLimitAsync(db, ct) is { } above && expense.Amount > above
            && await db.ExpenseProofs.CountAsync(p => p.ExpenseId == id, ct) <= 1)
            return ApiResults.Conflict($"Expenses above {Money(above)} need proof. Attach another file before removing this one.");

        db.ExpenseProofs.Remove(proof);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    /// <summary>
    /// Accepts a PNG, JPEG or WebP image or a PDF, up to <see cref="ExpenseProof.MaxBytes"/>. The type comes from the
    /// file's own bytes, never from its name or what the browser says, so nothing else is ever stored or served back.
    /// </summary>
    public static bool TryReadProof(ProofUpload? upload, out ExpenseProof proof, out string error)
    {
        proof = new ExpenseProof();
        error = "Attach a photo (JPG, PNG, WebP) or a PDF.";
        var dataUrl = upload?.DataUrl ?? "";
        var comma = dataUrl.IndexOf(',');
        if (comma < 0 || !dataUrl[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return false;

        var payload = dataUrl.AsSpan(comma + 1);
        var tooLarge = $"Each file must be under {ExpenseProof.MaxBytes / (1024 * 1024)} MB.";
        if (payload.Length > ExpenseProof.MaxBytes * 4L / 3 + 4) { error = tooLarge; return false; }
        var buffer = new byte[payload.Length * 3 / 4 + 3];
        if (!Convert.TryFromBase64Chars(payload, buffer, out var length) || length == 0) return false;
        if (length > ExpenseProof.MaxBytes) { error = tooLarge; return false; }
        var bytes = buffer[..length];

        var type = Sniff(bytes);
        if (type is null) return false;
        var name = Path.GetFileName((upload!.FileName ?? "").Trim());
        if (name.Length == 0) name = type == "application/pdf" ? "proof.pdf" : "proof";
        if (name.Length > 200) name = name[^200..];

        proof = new ExpenseProof { FileName = name, ContentType = type, SizeBytes = bytes.Length, Data = bytes };
        return true;
    }

    private static string? Sniff(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "image/png";
        if (data.StartsWith((byte[])[0xFF, 0xD8, 0xFF])) return "image/jpeg";
        if (data.Length > 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        if (data.StartsWith("%PDF-"u8)) return "application/pdf";
        return null;
    }
}
