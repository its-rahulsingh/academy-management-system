using System.Globalization;
using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

public enum ReportGrouping
{
    Day,
    Week,
    Month
}

/// <summary>Splits a date range into days, weeks (Monday to Sunday) or calendar months, clipped to the range.</summary>
public static class ReportPeriods
{
    /// <summary>Day-by-day lists stay readable on screen and in Excel up to about a year.</summary>
    public const int MaxDays = 400;

    public static ReportGrouping Parse(string? groupBy) =>
        Enum.TryParse<ReportGrouping>(groupBy, ignoreCase: true, out var g) && Enum.IsDefined(g) ? g : ReportGrouping.Month;

    public static DateOnly StartOf(DateOnly date, ReportGrouping grouping) => grouping switch
    {
        ReportGrouping.Day => date,
        ReportGrouping.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        _ => new DateOnly(date.Year, date.Month, 1)
    };

    private static DateOnly Next(DateOnly start, ReportGrouping grouping) => grouping switch
    {
        ReportGrouping.Day => start.AddDays(1),
        ReportGrouping.Week => start.AddDays(7),
        _ => start.AddMonths(1)
    };

    public sealed record Period(DateOnly Key, DateOnly From, DateOnly To, string Label);

    public static List<Period> Split(DateOnly from, DateOnly to, ReportGrouping grouping)
    {
        var periods = new List<Period>();
        for (var key = StartOf(from, grouping); key <= to; key = Next(key, grouping))
        {
            var start = key < from ? from : key;
            var lastDay = Next(key, grouping).AddDays(-1);
            var end = lastDay > to ? to : lastDay;
            periods.Add(new Period(key, start, end, Label(start, end, grouping)));
        }
        return periods;
    }

    private static string Label(DateOnly from, DateOnly to, ReportGrouping grouping)
    {
        var c = CultureInfo.InvariantCulture;
        return grouping switch
        {
            ReportGrouping.Day => from.ToString("ddd, dd MMM yyyy", c),
            ReportGrouping.Week when from == to => from.ToString("dd MMM yyyy", c),
            ReportGrouping.Week when from.Month == to.Month => $"{from.ToString("dd", c)}–{to.ToString("dd MMM yyyy", c)}",
            ReportGrouping.Week => $"{from.ToString("dd MMM", c)} – {to.ToString("dd MMM yyyy", c)}",
            _ => from.ToString("MMMM yyyy", c)
        };
    }

    public static string Title(ReportGrouping grouping) => grouping switch
    {
        ReportGrouping.Day => "Day by day",
        ReportGrouping.Week => "Week by week",
        _ => "Month by month"
    };
}

/// <summary>Academies here keep Indian time; rows stamped in UTC are shown and grouped by the Indian date.</summary>
public static class AcademyClock
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(5.5);

    /// <summary>Wall-clock Indian time, with no zone attached, so it is shown as is rather than converted again.</summary>
    public static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);

    public static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    /// <summary>The UTC instant at which the given Indian date begins.</summary>
    public static DateTime UtcStartOf(DateOnly date) => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue) - Offset, DateTimeKind.Utc);
}

/// <summary>Small helpers so every workbook looks the same: bold frozen header, filters, money and date formats.</summary>
public static class Excel
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string MoneyFormat = "₹#,##0.00;[Red]-₹#,##0.00";
    private const string DateFormat = "dd-mmm-yyyy";

    public static IXLWorksheet Sheet(XLWorkbook book, string name, IReadOnlyList<string> headers, IEnumerable<object?[]> rows, ISet<int>? moneyColumns = null)
    {
        var sheet = book.Worksheets.Add(name);
        for (var c = 0; c < headers.Count; c++) sheet.Cell(1, c + 1).Value = headers[c];
        var header = sheet.Range(1, 1, 1, Math.Max(1, headers.Count));
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F0EC");

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++) Set(sheet.Cell(r, c + 1), row[c], moneyColumns?.Contains(c) == true);
            r++;
        }

        sheet.SheetView.FreezeRows(1);
        if (r > 2) sheet.Range(1, 1, r - 1, headers.Count).SetAutoFilter();
        sheet.Columns(1, headers.Count).AdjustToContents(1, Math.Min(r, 500), 8, 60);
        return sheet;
    }

    /// <summary>Adds a bold "Total" row under the data.</summary>
    public static void TotalRow(IXLWorksheet sheet, object?[] cells, ISet<int> moneyColumns)
    {
        var r = sheet.LastRowUsed()!.RowNumber() + 1;
        for (var c = 0; c < cells.Length; c++) Set(sheet.Cell(r, c + 1), cells[c], moneyColumns.Contains(c));
        sheet.Row(r).Style.Font.Bold = true;
        sheet.Row(r).Style.Border.TopBorder = XLBorderStyleValues.Thin;
    }

    private static void Set(IXLCell cell, object? value, bool money)
    {
        switch (value)
        {
            case null: break;
            case decimal d:
                cell.Value = d;
                if (money) cell.Style.NumberFormat.Format = MoneyFormat;
                break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            case DateOnly date:
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                cell.Style.NumberFormat.Format = DateFormat;
                break;
            case DateTime dt:
                cell.Value = dt;
                cell.Style.NumberFormat.Format = "dd-mmm-yyyy hh:mm";
                break;
            // Text is stored as text, never as a formula, so names like "=SUM(...)" cannot run in Excel.
            default: cell.SetValue(value.ToString() ?? ""); break;
        }
    }

    public static IResult File(XLWorkbook book, string fileName)
    {
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return Results.File(stream.ToArray(), ContentType, fileName);
    }
}

/// <summary>
/// The day-by-day record of what happened in the academy: admissions, applications, fees, refunds, discounts,
/// expenses, faculty pay, teaching time and tests. Voided money entries are listed but left out of totals.
/// </summary>
public static class ActivityReport
{
    private const int MaxOnScreen = 1000;
    private const int MaxInExcel = 50_000;

    public static void MapActivityReportEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/reports/activity").RequirePermission(Permissions.ReportsActivity);
        group.MapGet("", Get);
        group.MapGet("/excel", Download);
    }

    public sealed record Event(
        DateOnly Date, DateTime EnteredAt, string Type, string Title, string Detail, decimal? Amount, string Flow, string Status, string By, int? Minutes = null);

    private sealed record Totals(
        int Admissions, int Applications, decimal Collections, decimal Refunds, decimal Discounts, decimal Surcharges,
        decimal Expenses, decimal Payouts, int TeachingMinutes, decimal TeachingPay, int Tests)
    {
        public decimal NetCash => Collections - Refunds - Expenses - Payouts;
    }

    private static (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
    {
        var end = to ?? Validate.Today;
        var start = from ?? end;
        return start <= end ? (start, end) : (end, start);
    }

    private static async Task<List<Event>> LoadAsync(AcademyDbContext db, DateOnly from, DateOnly to, int limit, CancellationToken ct)
    {
        var fromUtc = AcademyClock.UtcStartOf(from);
        var toUtc = AcademyClock.UtcStartOf(to.AddDays(1));
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        string By(Guid? id) => id is { } v && users.TryGetValue(v, out var name) ? name : "";
        var events = new List<Event>();

        var admissions = await db.Students.AsNoTracking()
            .Where(s => s.AdmissionDate >= from && s.AdmissionDate <= to)
            .OrderBy(s => s.AdmissionDate).Take(limit)
            .Select(s => new { s.AdmissionDate, s.CreatedAtUtc, s.FullName, Course = s.Course.Name, s.FeeAmount, Year = s.AcademicYear.Name, s.CreatedByUserId })
            .ToListAsync(ct);
        events.AddRange(admissions.Select(s => new Event(s.AdmissionDate, AcademyClock.ToLocal(s.CreatedAtUtc), "Admission", s.FullName,
            $"{s.Course} · fee {s.FeeAmount:0.00} · {s.Year}", null, "", "", By(s.CreatedByUserId))));

        var applications = await db.AdmissionApplications.AsNoTracking()
            .Where(a => a.CreatedAtUtc >= fromUtc && a.CreatedAtUtc < toUtc)
            .OrderBy(a => a.CreatedAtUtc).Take(limit)
            .Select(a => new { a.CreatedAtUtc, a.FullName, a.Phone, Course = a.Course.Name, a.Status })
            .ToListAsync(ct);
        events.AddRange(applications.Select(a => new Event(AcademyClock.LocalDate(a.CreatedAtUtc), AcademyClock.ToLocal(a.CreatedAtUtc),
            "Online application", a.FullName, $"{a.Course} · {a.Phone}", null, "", a.Status.ToString(), "Admission form")));

        var payments = await db.StudentPayments.AsNoTracking()
            .Where(p => p.PaidOn >= from && p.PaidOn <= to)
            .OrderBy(p => p.PaidOn).ThenBy(p => p.ReceiptNumber).Take(limit)
            .Select(p => new { p.PaidOn, p.CreatedAtUtc, p.ReceiptNumber, Student = p.Student.FullName, p.Kind, p.Method, p.Amount, p.Status, p.Notes, p.CreatedByUserId })
            .ToListAsync(ct);
        events.AddRange(payments.Select(p => new Event(p.PaidOn, AcademyClock.ToLocal(p.CreatedAtUtc),
            p.Kind == PaymentKind.Payment ? "Fee payment" : "Refund", p.Student,
            Join(StudentLedger.ReceiptLabel(p.ReceiptNumber), p.Method, p.Notes), p.Amount,
            p.Kind == PaymentKind.Payment ? "In" : "Out", p.Status.ToString(), By(p.CreatedByUserId))));

        var adjustments = await db.FeeAdjustments.AsNoTracking()
            .Where(a => a.CreatedAtUtc >= fromUtc && a.CreatedAtUtc < toUtc)
            .OrderBy(a => a.CreatedAtUtc).Take(limit)
            .Select(a => new { a.CreatedAtUtc, Student = a.Student.FullName, a.Kind, a.Amount, a.Reason, a.Status, a.CreatedByUserId })
            .ToListAsync(ct);
        events.AddRange(adjustments.Select(a => new Event(AcademyClock.LocalDate(a.CreatedAtUtc), AcademyClock.ToLocal(a.CreatedAtUtc),
            a.Kind.ToString(), a.Student, a.Reason, a.Amount, "", a.Status.ToString(), By(a.CreatedByUserId))));

        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.SpentOn >= from && e.SpentOn <= to)
            .OrderBy(e => e.SpentOn).Take(limit)
            .Select(e => new { e.SpentOn, e.CreatedAtUtc, e.Title, e.Category, e.Method, e.Amount, e.Status, e.Notes, e.CreatedByUserId })
            .ToListAsync(ct);
        events.AddRange(expenses.Select(e => new Event(e.SpentOn, AcademyClock.ToLocal(e.CreatedAtUtc), "Expense", e.Title,
            Join(e.Category, e.Method, e.Notes), e.Amount, "Out", e.Status.ToString(), By(e.CreatedByUserId))));

        var payouts = await db.FacultyPayouts.AsNoTracking()
            .Where(p => p.PaidOn >= from && p.PaidOn <= to)
            .OrderBy(p => p.PaidOn).Take(limit)
            .Select(p => new { p.PaidOn, p.CreatedAtUtc, Faculty = p.Faculty.FullName, p.Method, p.Amount, p.Status, p.Notes, p.CreatedByUserId })
            .ToListAsync(ct);
        events.AddRange(payouts.Select(p => new Event(p.PaidOn, AcademyClock.ToLocal(p.CreatedAtUtc), "Faculty payout", p.Faculty,
            Join(p.Method, p.Notes), p.Amount, "Out", p.Status.ToString(), By(p.CreatedByUserId))));

        var sessions = await db.FacultySessions.AsNoTracking()
            .Where(s => s.TaughtOn >= from && s.TaughtOn <= to)
            .OrderBy(s => s.TaughtOn).ThenBy(s => s.StartTime).Take(limit)
            .Select(s => new
            {
                s.TaughtOn, s.CreatedAtUtc, Faculty = s.Faculty.FullName, s.StartTime, s.EndTime, s.Minutes, s.Amount, s.Topic,
                Subject = s.Subject != null ? s.Subject.Name : null, s.Status, s.Review, s.CreatedByUserId
            })
            .ToListAsync(ct);
        events.AddRange(sessions.Select(s => new Event(s.TaughtOn, AcademyClock.ToLocal(s.CreatedAtUtc), "Teaching time", s.Faculty,
            Join(s.StartTime is { } a && s.EndTime is { } b ? $"{a:HH\\:mm}–{b:HH\\:mm}" : null, $"{s.Minutes} min", s.Subject, s.Topic),
            s.Amount, "", s.Status == LedgerStatus.Voided ? "Withdrawn" : s.Review == SessionReview.Approved ? "Approved" : s.Review == SessionReview.Pending ? "Waiting approval" : "Rejected",
            By(s.CreatedByUserId), s.Minutes)));

        var tests = await db.Tests.AsNoTracking()
            .Where(t => t.TestDate >= from && t.TestDate <= to)
            .OrderBy(t => t.TestDate).Take(limit)
            .Select(t => new { t.TestDate, t.CreatedAtUtc, t.Title, Course = t.Course.Name, Subject = t.Subject.Name, t.MaxMarks, t.CreatedByUserId })
            .ToListAsync(ct);
        events.AddRange(tests.Select(t => new Event(t.TestDate, AcademyClock.ToLocal(t.CreatedAtUtc), "Test", t.Title,
            $"{t.Course} · {t.Subject} · out of {t.MaxMarks:0.##}", null, "", "", By(t.CreatedByUserId))));

        return events.OrderBy(e => e.Date).ThenBy(e => e.EnteredAt).ToList();
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static bool Live(Event e) => e.Status is not ("Voided" or "Withdrawn" or "Rejected");

    private static Totals Sum(IEnumerable<Event> source)
    {
        var events = source.ToList();
        decimal Money(string type) => events.Where(e => e.Type == type && Live(e)).Sum(e => e.Amount ?? 0);
        var teaching = events.Where(e => e.Type == "Teaching time" && e.Status == "Approved").ToList();
        return new Totals(
            events.Count(e => e.Type == "Admission"),
            events.Count(e => e.Type == "Online application"),
            Money("Fee payment"), Money("Refund"), Money(nameof(AdjustmentKind.Discount)), Money(nameof(AdjustmentKind.Surcharge)),
            Money("Expense"), Money("Faculty payout"),
            teaching.Sum(e => e.Minutes ?? 0), teaching.Sum(e => e.Amount ?? 0),
            events.Count(e => e.Type == "Test"));
    }

    private static List<(ReportPeriods.Period Period, Totals Totals)> ByPeriod(List<Event> events, DateOnly from, DateOnly to, ReportGrouping grouping)
    {
        var lookup = events.ToLookup(e => ReportPeriods.StartOf(e.Date, grouping));
        return ReportPeriods.Split(from, to, grouping).Select(p => (p, Sum(lookup[p.Key]))).ToList();
    }

    private static IResult? CheckRange(DateOnly from, DateOnly to, ReportGrouping grouping) =>
        grouping == ReportGrouping.Day && to.DayNumber - from.DayNumber >= ReportPeriods.MaxDays
            ? ApiResults.Bad($"Day by day covers at most {ReportPeriods.MaxDays} days. Pick a shorter range, or group by week or month.")
            : null;

    private static async Task<IResult> Get(DateOnly? from, DateOnly? to, string? groupBy, AcademyDbContext db, CancellationToken ct)
    {
        var (start, end) = Range(from, to);
        var grouping = ReportPeriods.Parse(groupBy);
        if (CheckRange(start, end, grouping) is { } error) return error;

        var events = await LoadAsync(db, start, end, MaxInExcel, ct);
        var totals = Sum(events);
        return Results.Ok(new
        {
            from = start,
            to = end,
            groupBy = grouping,
            title = ReportPeriods.Title(grouping),
            totals = Shape(totals),
            periods = ByPeriod(events, start, end, grouping).Select(x => new { x.Period.Label, x.Period.From, x.Period.To, totals = Shape(x.Totals) }),
            events = events.OrderByDescending(e => e.Date).ThenByDescending(e => e.EnteredAt).Take(MaxOnScreen),
            eventCount = events.Count,
            truncated = events.Count > MaxOnScreen
        });
    }

    private static object Shape(Totals t) => new
    {
        t.Admissions, t.Applications, t.Collections, t.Refunds, t.Discounts, t.Surcharges, t.Expenses, t.Payouts,
        t.TeachingMinutes, t.TeachingPay, t.Tests, t.NetCash
    };

    private static async Task<IResult> Download(
        DateOnly? from, DateOnly? to, string? groupBy, AcademyDbContext db, ICurrentUser currentUser, TenantAccessService access, CancellationToken ct)
    {
        var (start, end) = Range(from, to);
        var grouping = ReportPeriods.Parse(groupBy);
        if (CheckRange(start, end, grouping) is { } error) return error;

        var events = await LoadAsync(db, start, end, MaxInExcel, ct);
        var academy = (await access.GetAsync(currentUser.TenantId!.Value, ct))?.TenantName ?? "Academy";
        using var book = new XLWorkbook();

        var money = new HashSet<int> { 3, 4, 5, 6, 7, 8, 10, 12 };
        var periodSheet = Excel.Sheet(book, ReportPeriods.Title(grouping),
            ["Period", "Admissions", "Online applications", "Fees collected", "Refunds", "Discounts", "Surcharges", "Expenses",
             "Faculty payouts", "Teaching minutes", "Teaching pay (approved)", "Tests", "Net cash"],
            ByPeriod(events, start, end, grouping).Select(x => Row(x.Period.Label, x.Totals)), money);
        Excel.TotalRow(periodSheet, Row("Total", Sum(events)), money);

        Excel.Sheet(book, "Activity",
            ["Date", "Entered at", "Type", "Name", "Details", "Amount", "Money", "Status", "Entered by"],
            events.Select(e => new object?[] { e.Date, e.EnteredAt, e.Type, e.Title, e.Detail, e.Amount, e.Flow, e.Status, e.By }),
            new HashSet<int> { 5 });

        var about = book.Worksheets.Add("About");
        about.Cell(1, 1).Value = $"{academy} — activity report";
        about.Cell(1, 1).Style.Font.Bold = true;
        about.Cell(1, 1).Style.Font.FontSize = 14;
        about.Cell(2, 1).Value = $"{start:dd MMM yyyy} to {end:dd MMM yyyy}, {ReportPeriods.Title(grouping).ToLowerInvariant()}";
        about.Cell(3, 1).Value = $"Downloaded {AcademyClock.ToLocal(DateTime.UtcNow):dd MMM yyyy HH:mm} (Indian time)";
        about.Cell(5, 1).Value = "Voided money entries, withdrawn and rejected teaching time are listed on the Activity sheet but left out of the totals.";
        about.Cell(6, 1).Value = "Net cash = fees collected − refunds − expenses − faculty payouts. It is cash in and out, not accounting profit.";
        if (events.Count >= MaxInExcel) about.Cell(7, 1).Value = $"Only the first {MaxInExcel:N0} entries of each kind are included. Pick a shorter range for the rest.";

        return Excel.File(book, $"activity-{start:yyyyMMdd}-{end:yyyyMMdd}-{grouping.ToString().ToLowerInvariant()}.xlsx");
    }

    private static object?[] Row(string label, Totals t) =>
    [
        label, t.Admissions, t.Applications, t.Collections, t.Refunds, t.Discounts, t.Surcharges, t.Expenses, t.Payouts,
        t.TeachingMinutes, t.TeachingPay, t.Tests, t.NetCash
    ];
}
