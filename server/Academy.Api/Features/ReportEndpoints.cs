using System.Globalization;
using System.Text;
using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>Fee position of one course. Built from grouped SQL so it stays cheap as student numbers grow.</summary>
public sealed record CourseFinance(Guid CourseId, string CourseName, int Students, decimal Fees, decimal Discounts, decimal Surcharges, decimal Received, decimal Refunded)
{
    public decimal Payable => Math.Max(0, Fees - Discounts + Surcharges);
    public decimal Collected => Received - Refunded;
    public decimal Pending => Math.Max(0, Payable - Collected);
}

public static class FinanceQueries
{
    /// <summary>Fee position per course for the students admitted in the given year (all years when null).</summary>
    public static async Task<List<CourseFinance>> CourseFinancesAsync(this AcademyDbContext db, Guid? yearId, CancellationToken ct)
    {
        var courses = await db.Courses.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var fees = await db.Students.Where(s => yearId == null || s.AcademicYearId == yearId).GroupBy(s => s.CourseId)
            .Select(g => new { CourseId = g.Key, Students = g.Count(), Fees = g.Sum(s => s.FeeAmount) })
            .ToDictionaryAsync(x => x.CourseId, ct);
        var adjustments = await db.FeeAdjustments
            .Where(a => a.Status == LedgerStatus.Posted && (yearId == null || a.Student.AcademicYearId == yearId))
            .GroupBy(a => new { a.Student.CourseId, a.Kind })
            .Select(g => new { g.Key.CourseId, g.Key.Kind, Amount = g.Sum(a => a.Amount) })
            .ToListAsync(ct);
        var payments = await db.StudentPayments
            .Where(p => p.Status == LedgerStatus.Posted && (yearId == null || p.Student.AcademicYearId == yearId))
            .GroupBy(p => new { p.Student.CourseId, p.Kind })
            .Select(g => new { g.Key.CourseId, g.Key.Kind, Amount = g.Sum(p => p.Amount) })
            .ToListAsync(ct);

        return courses.OrderBy(c => c.Name).Select(c => new CourseFinance(
            c.Id, c.Name,
            fees.TryGetValue(c.Id, out var f) ? f.Students : 0,
            f?.Fees ?? 0,
            adjustments.Where(a => a.CourseId == c.Id && a.Kind == AdjustmentKind.Discount).Sum(a => a.Amount),
            adjustments.Where(a => a.CourseId == c.Id && a.Kind == AdjustmentKind.Surcharge).Sum(a => a.Amount),
            payments.Where(p => p.CourseId == c.Id && p.Kind == PaymentKind.Payment).Sum(p => p.Amount),
            payments.Where(p => p.CourseId == c.Id && p.Kind == PaymentKind.Refund).Sum(p => p.Amount))).ToList();
    }
}

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this RouteGroupBuilder academy) =>
        academy.MapGet("/dashboard", Dashboard).RequireAuthorization();

    private static async Task<IResult> Dashboard(HttpContext http, AcademyDbContext db, AcademicYearScope years, CancellationToken ct)
    {
        var year = await years.GetAsync(ct);
        var counts = new
        {
            totalStudents = await db.Students.InYear(year).CountAsync(ct),
            activeStudents = await db.Students.InYear(year).CountAsync(s => s.Active, ct),
            totalCourses = await db.Courses.CountAsync(ct),
            totalFaculty = await db.Faculty.CountAsync(ct)
        };

        // Money figures are omitted entirely (not zeroed) for roles that may not see them.
        if (!http.User.Has(Permissions.DashboardFinance)) return Results.Ok(new { counts, finance = (object?)null });

        var courses = await db.CourseFinancesAsync(year?.Id, ct);
        // Faculty agreements are not tied to a year, so what is still owed to faculty is an all-time figure.
        var agreed = await FacultyEndpoints.TotalEarnedAsync(db, ct);
        var paidOutEver = await db.FacultyPayouts.Where(p => p.Status == LedgerStatus.Posted).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var paidOut = await db.FacultyPayouts.Where(p => p.Status == LedgerStatus.Posted).Between(year, p => p.PaidOn)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var expenses = await db.Expenses.Where(e => e.Status == LedgerStatus.Posted).Between(year, e => e.SpentOn)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0;
        // Cash received during the year, whichever year the paying student was admitted in.
        var payments = db.StudentPayments.Where(p => p.Status == LedgerStatus.Posted).Between(year, p => p.PaidOn);
        var received = await payments.Where(p => p.Kind == PaymentKind.Payment).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var refunded = await payments.Where(p => p.Kind == PaymentKind.Refund).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        // Dues still owed by students admitted in earlier years, so they are not forgotten when a new year starts.
        var earlierDues = year is null ? 0 : await db.StudentBalances()
            .Where(s => s.AcademicYearId != year.Id && db.AcademicYears.Any(y => y.Id == s.AcademicYearId && y.StartDate < year.Start))
            .WithStatus("Due")
            .SumAsync(s => (decimal?)(s.FeeAmount - s.Discounts + s.Surcharges - (s.Received - s.Refunded)), ct) ?? 0;
        var recent = await db.StudentPayments.AsNoTracking()
            .Where(p => p.Status == LedgerStatus.Posted).Between(year, p => p.PaidOn)
            .OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.ReceiptNumber)
            .Take(6)
            .Select(p => new { p.Id, StudentName = p.Student.FullName, p.Kind, p.Amount, p.PaidOn, p.Method })
            .ToListAsync(ct);

        var collected = received - refunded;
        return Results.Ok(new
        {
            counts,
            finance = new
            {
                totalRevenue = collected,
                pendingFees = courses.Sum(c => c.Pending),
                earlierYearsPending = earlierDues,
                facultyPayable = Math.Max(0, agreed - paidOutEver),
                facultyPaidOut = paidOut,
                totalExpenses = expenses,
                // Cash basis: money in minus money out during the year. Not accounting profit.
                netCashBalance = collected - expenses - paidOut,
                recentPayments = recent,
                coursePerformance = courses.Select(c => new { c.CourseName, c.Students, c.Collected, c.Pending })
            }
        });
    }
}

public static class ReportEndpoints
{
    private const int MaxExportRows = 50_000;

    public static void MapReportEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/reports").RequirePermission(Permissions.ReportsRead);
        group.MapGet("/summary", Summary);
        group.MapGet("/export/{kind}", Export);
    }

    /// <summary>Explicit dates win; otherwise the selected academic year; with no year, the last twelve months.</summary>
    private static (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to, YearRange? year)
    {
        var end = to ?? year?.End ?? Validate.Today;
        var start = from ?? year?.Start ?? new DateOnly(end.Year, end.Month, 1).AddMonths(-11);
        return start <= end ? (start, end) : (end, start);
    }

    private static async Task<IResult> Summary(DateOnly? from, DateOnly? to, AcademyDbContext db, AcademicYearScope years, CancellationToken ct)
    {
        var year = await years.GetAsync(ct);
        var (start, end) = Range(from, to, year);
        var payments = db.StudentPayments.Where(p => p.Status == LedgerStatus.Posted && p.PaidOn >= start && p.PaidOn <= end);
        var expenses = db.Expenses.Where(e => e.Status == LedgerStatus.Posted && e.SpentOn >= start && e.SpentOn <= end);
        var payouts = db.FacultyPayouts.Where(p => p.Status == LedgerStatus.Posted && p.PaidOn >= start && p.PaidOn <= end);

        var paymentMonths = await payments.GroupBy(p => new { p.PaidOn.Year, p.PaidOn.Month, p.Kind })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Kind, Amount = g.Sum(p => p.Amount) }).ToListAsync(ct);
        var expenseMonths = await expenses.GroupBy(e => new { e.SpentOn.Year, e.SpentOn.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Amount = g.Sum(e => e.Amount) }).ToListAsync(ct);
        var payoutMonths = await payouts.GroupBy(p => new { p.PaidOn.Year, p.PaidOn.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Amount = g.Sum(p => p.Amount) }).ToListAsync(ct);
        var byMethod = await payments.Where(p => p.Kind == PaymentKind.Payment).GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Amount = g.Sum(p => p.Amount), Count = g.Count() }).ToListAsync(ct);
        var byCategory = await expenses.GroupBy(e => e.Category)
            .Select(g => new { Category = g.Key, Amount = g.Sum(e => e.Amount), Count = g.Count() }).ToListAsync(ct);

        var months = new List<object>();
        for (var month = new DateOnly(start.Year, start.Month, 1); month <= end; month = month.AddMonths(1))
        {
            var received = paymentMonths.Where(m => m.Year == month.Year && m.Month == month.Month && m.Kind == PaymentKind.Payment).Sum(m => m.Amount);
            var refunded = paymentMonths.Where(m => m.Year == month.Year && m.Month == month.Month && m.Kind == PaymentKind.Refund).Sum(m => m.Amount);
            var spent = expenseMonths.Where(m => m.Year == month.Year && m.Month == month.Month).Sum(m => m.Amount);
            var paid = payoutMonths.Where(m => m.Year == month.Year && m.Month == month.Month).Sum(m => m.Amount);
            months.Add(new { month = month.ToString("yyyy-MM"), collections = received - refunded, expenses = spent, payouts = paid, net = received - refunded - spent - paid });
        }

        var collections = paymentMonths.Where(m => m.Kind == PaymentKind.Payment).Sum(m => m.Amount);
        var refunds = paymentMonths.Where(m => m.Kind == PaymentKind.Refund).Sum(m => m.Amount);
        var totalExpenses = expenseMonths.Sum(m => m.Amount);
        var totalPayouts = payoutMonths.Sum(m => m.Amount);
        var courses = await db.CourseFinancesAsync(year?.Id, ct);

        return Results.Ok(new
        {
            from = start,
            to = end,
            collections,
            refunds,
            netCollections = collections - refunds,
            expenses = totalExpenses,
            payouts = totalPayouts,
            netCash = collections - refunds - totalExpenses - totalPayouts,
            byMethod = byMethod.OrderByDescending(m => m.Amount),
            expensesByCategory = byCategory.OrderByDescending(c => c.Amount),
            months,
            // Outstanding fees are a position as of now for the selected year's students, not a flow within the date range.
            courses = courses.Select(c => new { c.CourseName, c.Students, c.Payable, c.Collected, c.Pending })
        });
    }

    private static async Task<IResult> Export(string kind, DateOnly? from, DateOnly? to, AcademyDbContext db, AcademicYearScope years, CancellationToken ct)
    {
        var year = await years.GetAsync(ct);
        var (start, end) = Range(from, to, year);
        var csv = new Csv();

        switch (kind)
        {
            case "payments":
                csv.Row("Receipt", "Date", "Student", "Type", "Method", "Amount", "Status", "Notes", "Void reason");
                var payments = await db.StudentPayments.AsNoTracking()
                    .Where(p => p.PaidOn >= start && p.PaidOn <= end)
                    .OrderBy(p => p.PaidOn).ThenBy(p => p.ReceiptNumber).Take(MaxExportRows)
                    .Select(p => new { p.ReceiptNumber, p.PaidOn, Student = p.Student.FullName, p.Kind, p.Method, p.Amount, p.Status, p.Notes, p.VoidReason })
                    .ToListAsync(ct);
                foreach (var p in payments)
                    csv.Row(StudentLedger.ReceiptLabel(p.ReceiptNumber), p.PaidOn, p.Student, p.Kind, p.Method, p.Amount, p.Status, p.Notes, p.VoidReason);
                break;

            case "expenses":
                csv.Row("Date", "Title", "Category", "Method", "Amount", "Status", "Notes", "Void reason");
                var expenses = await db.Expenses.AsNoTracking()
                    .Where(e => e.SpentOn >= start && e.SpentOn <= end)
                    .OrderBy(e => e.SpentOn).Take(MaxExportRows).ToListAsync(ct);
                foreach (var e in expenses) csv.Row(e.SpentOn, e.Title, e.Category, e.Method, e.Amount, e.Status, e.Notes, e.VoidReason);
                break;

            case "payouts":
                csv.Row("Date", "Faculty", "Method", "Amount", "Status", "Notes", "Void reason");
                var payouts = await db.FacultyPayouts.AsNoTracking()
                    .Where(p => p.PaidOn >= start && p.PaidOn <= end)
                    .OrderBy(p => p.PaidOn).Take(MaxExportRows)
                    .Select(p => new { p.PaidOn, Faculty = p.Faculty.FullName, p.Method, p.Amount, p.Status, p.Notes, p.VoidReason })
                    .ToListAsync(ct);
                foreach (var p in payouts) csv.Row(p.PaidOn, p.Faculty, p.Method, p.Amount, p.Status, p.Notes, p.VoidReason);
                break;

            case "students":
                csv.Row("Student", "Academic year", "Email", "Phone", "Course", "Admission", "Fee", "Discounts", "Surcharges", "Payable", "Paid", "Pending", "Status", "Active");
                var students = await db.StudentBalances()
                    .Where(s => year == null || s.AcademicYearId == year.Id)
                    .OrderBy(s => s.FullName).Take(MaxExportRows).ToListAsync(ct);
                foreach (var s in students)
                    csv.Row(s.FullName, s.AcademicYearName, s.Email, s.Phone, s.CourseName, s.AdmissionDate, s.FeeAmount, s.Discounts, s.Surcharges, s.Payable, s.Paid, s.Pending, s.Status, s.Active ? "Yes" : "No");
                break;

            default:
                return Results.NotFound();
        }

        // The BOM makes Excel open the file as UTF-8, so names and the rupee sign survive.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", $"{kind}-{start:yyyyMMdd}-{end:yyyyMMdd}.csv");
    }

    private sealed class Csv
    {
        private readonly StringBuilder _builder = new();

        public void Row(params object?[] cells) => _builder.AppendJoin(',', cells.Select(Cell)).Append("\r\n");

        private static string Cell(object? value)
        {
            var text = value switch
            {
                null => "",
                DateOnly d => d.ToString("yyyy-MM-dd"),
                decimal m => m.ToString("0.00", CultureInfo.InvariantCulture),
                _ => value.ToString() ?? ""
            };
            // A cell starting with = + - @ is run as a formula by spreadsheet apps; neutralise user-entered text.
            if (value is string && text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
            return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
        }

        public override string ToString() => _builder.ToString();
    }
}

public static class AuditEndpoints
{
    public sealed class AuditListQuery : PageQuery
    {
        public string? EntityType { get; set; }
        public string? Search { get; set; }
    }

    public static void MapAuditEndpoints(this RouteGroupBuilder academy) =>
        academy.MapGet("/audit", List).RequirePermission(Permissions.AuditRead);

    private static async Task<IResult> List([AsParameters] AuditListQuery query, AcademyDbContext db, CancellationToken ct)
    {
        var logs = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.EntityType)) logs = logs.Where(a => a.EntityType == query.EntityType);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = QueryExtensions.Like(query.Search);
            logs = logs.Where(a => EF.Functions.ILike(a.UserName, like) || EF.Functions.ILike(a.Action, like));
        }

        return Results.Ok(await logs
            .OrderByDescending(a => a.Id)
            .Select(a => new { a.Id, a.UserName, a.Action, a.EntityType, a.EntityId, a.Changes, a.CreatedAtUtc })
            .ToPagedAsync(query, ct));
    }
}
