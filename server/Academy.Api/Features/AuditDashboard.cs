using System.Globalization;
using System.Text.Json;
using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// Turns the activity log into a dashboard: headline numbers, activity over time, the busiest hours, who did what,
/// and "needs attention" flags for actions worth a second look (cancelled payments, lowered fees, large discounts,
/// mass deletions, late-night work, new sign-in networks, password guessing). Also the history of a single record.
/// Flags are worked out from the log each time; only the Super Admin's "reviewed" marks are stored.
/// </summary>
public static class AuditDashboard
{
    private const int MaxRows = 100_000;
    private const int MaxFlags = 300;
    private const string Login = "Login";
    private const string LoginFailed = "LoginFailed";
    /// <summary>Night hours in academy time: from 10 PM to before 6 AM.</summary>
    private const int NightFrom = 22, NightTo = 6;
    private const int BurstSize = 5;
    private static readonly TimeSpan BurstWindow = TimeSpan.FromMinutes(10);
    private const int FailedAttemptsFlag = 3;

    private static readonly HashSet<string> MoneyTypes =
        [nameof(StudentPayment), nameof(FeeAdjustment), nameof(Expense), nameof(FacultyPayout)];

    public sealed record ReviewRequest(string? Key, bool Reviewed);
    public sealed record SettingsRequest(decimal LargeAmountAlert);

    public static void MapAuditDashboardEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/audit");
        group.MapGet("/dashboard", Dashboard).RequirePermission(Permissions.AuditRead);
        group.MapGet("/history", History).RequirePermission(Permissions.AuditRead);
        group.MapPost("/flags/review", Review).RequirePermission(Permissions.ReportsActivity);
        group.MapPut("/settings", SaveSettings).RequirePermission(Permissions.SettingsManage);
    }

    private sealed record Row(long Id, Guid? UserId, string UserName, string Action, string EntityType, string EntityId, string? Changes, string? Ip, DateTime At)
    {
        public DateTime Local => AcademyClock.ToLocal(At);
        public bool IsSignIn => Action is Login or LoginFailed;
        public bool IsRemoval => Action is "Deleted" or "Voided";
        public bool AfterHours => Local.Hour >= NightFrom || Local.Hour < NightTo;

        private JsonElement? _json;
        private bool _parsed;
        public JsonElement? Json
        {
            get
            {
                if (_parsed) return _json;
                _parsed = true;
                try { _json = Changes is null ? null : JsonDocument.Parse(Changes).RootElement; }
                catch (JsonException) { _json = null; }
                return _json;
            }
        }

        /// <summary>The value a Created/Deleted row recorded, or the new value of an Updated row.</summary>
        public JsonElement? Value(string field)
        {
            if (Json is not { ValueKind: JsonValueKind.Object } j || !j.TryGetProperty(field, out var v)) return null;
            return v.ValueKind == JsonValueKind.Object && v.TryGetProperty("new", out var n) ? n : v;
        }

        public (JsonElement Old, JsonElement New)? Change(string field) =>
            Json is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Object
            && v.TryGetProperty("old", out var o) && v.TryGetProperty("new", out var n)
                ? (o, n) : null;
    }

    public sealed class Flag
    {
        public string Key { get; init; } = "";
        /// <summary>red, orange or yellow.</summary>
        public string Severity { get; init; } = "yellow";
        public string Title { get; init; } = "";
        public string Detail { get; init; } = "";
        public string UserName { get; init; } = "";
        public DateTime AtUtc { get; init; }
        public string? EntityType { get; init; }
        public string? EntityId { get; init; }
        public bool Reviewed { get; set; }
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
    }

    private static int Rank(string severity) => severity switch { "red" => 0, "orange" => 1, _ => 2 };

    private static async Task<IResult> Dashboard(
        DateOnly? from, DateOnly? to, string? groupBy, bool? showReviewed, AcademyDbContext db, CancellationToken ct)
    {
        var end = to ?? Validate.Today;
        var start = from ?? end.AddDays(-29);
        if (start > end) (start, end) = (end, start);
        var grouping = ReportPeriods.Parse(groupBy ?? "Day");
        if (grouping == ReportGrouping.Day && end.DayNumber - start.DayNumber >= ReportPeriods.MaxDays)
            return ApiResults.Bad($"Day by day covers at most {ReportPeriods.MaxDays} days. Choose week or month.");

        var fromUtc = AcademyClock.UtcStartOf(start);
        var toUtc = AcademyClock.UtcStartOf(end.AddDays(1));
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.CreatedAtUtc >= fromUtc && a.CreatedAtUtc < toUtc)
            .OrderBy(a => a.Id).Take(MaxRows)
            .Select(a => new Row(a.Id, a.UserId, a.UserName, a.Action, a.EntityType, a.EntityId, a.Changes, a.IpAddress, a.CreatedAtUtc))
            .ToListAsync(ct);

        var span = toUtc - fromUtc;
        var previousFrom = fromUtc - span;
        var previousActions = await db.AuditLogs.CountAsync(
            a => a.CreatedAtUtc >= previousFrom && a.CreatedAtUtc < fromUtc && a.Action != Login && a.Action != LoginFailed, ct);

        var users = await db.Users.AsNoTracking().Select(u => new { u.Id, u.DisplayName, u.Role }).ToDictionaryAsync(u => u.Id, ct);
        string NameOf(Row r) => r.UserId is { } id && users.TryGetValue(id, out var u) ? u.DisplayName : r.UserName;
        string? RoleOf(Row r) => r.UserId is { } id && users.TryGetValue(id, out var u) ? u.Role : null;

        var actions = rows.Where(r => !r.IsSignIn).ToList();

        var cards = new
        {
            actions = actions.Count,
            previousActions,
            activeUsers = actions.Where(r => r.UserId is not null).Select(r => r.UserId).Distinct().Count(),
            moneyEdits = actions.Count(IsMoneyEdit),
            removals = actions.Count(r => r.IsRemoval),
            afterHours = actions.Count(r => r.AfterHours),
            signIns = rows.Count(r => r.Action == Login),
            failedSignIns = rows.Count(r => r.Action == LoginFailed)
        };

        // Activity over time: created / changed / removed per period, by the academy's date.
        var byDate = actions.GroupBy(r => DateOnly.FromDateTime(r.Local)).ToDictionary(g => g.Key, g => g.ToList());
        var timeline = ReportPeriods.Split(start, end, grouping).Select(p =>
        {
            var inPeriod = byDate.Where(d => d.Key >= p.From && d.Key <= p.To).SelectMany(d => d.Value).ToList();
            return new
            {
                p.Label, from = p.From, to = p.To,
                created = inPeriod.Count(r => r.Action == "Created"),
                changed = inPeriod.Count(r => r.Action == "Updated"),
                removed = inPeriod.Count(r => r.IsRemoval),
                total = inPeriod.Count
            };
        }).ToList();

        // Busiest hours: Monday-first rows of 24 hourly counts.
        var heatmap = Enumerable.Range(0, 7).Select(_ => new int[24]).ToArray();
        foreach (var r in actions) heatmap[((int)r.Local.DayOfWeek + 6) % 7][r.Local.Hour]++;

        var byUser = actions.GroupBy(r => r.UserId?.ToString() ?? r.UserName)
            .Select(g => new
            {
                name = NameOf(g.First()),
                role = RoleOf(g.First()),
                created = g.Count(r => r.Action == "Created"),
                changed = g.Count(r => r.Action == "Updated"),
                removed = g.Count(r => r.IsRemoval),
                afterHours = g.Count(r => r.AfterHours),
                total = g.Count()
            })
            .OrderByDescending(u => u.total).Take(15).ToList();

        var byArea = actions.GroupBy(r => r.EntityType)
            .Select(g => new { entityType = g.Key, count = g.Count() })
            .OrderByDescending(a => a.count).ToList();

        var alert = await db.Tenants.Where(t => t.Id == db.CurrentTenantId).Select(t => t.LargeAmountAlert).FirstOrDefaultAsync(ct);
        var flags = await FlagsAsync(db, rows, actions, fromUtc, alert, NameOf, RoleOf, ct);

        var keys = flags.Select(f => f.Key).ToList();
        var reviews = await db.AuditFlagReviews.AsNoTracking().Where(r => keys.Contains(r.Key)).ToDictionaryAsync(r => r.Key, ct);
        foreach (var flag in flags)
        {
            if (!reviews.TryGetValue(flag.Key, out var review)) continue;
            flag.Reviewed = true;
            flag.ReviewedBy = review.ReviewedByName;
            flag.ReviewedAtUtc = review.ReviewedAtUtc;
        }
        var open = flags.Where(f => !f.Reviewed).ToList();

        return Results.Ok(new
        {
            from = start,
            to = end,
            groupBy = grouping.ToString(),
            truncated = rows.Count == MaxRows,
            largeAmountAlert = alert,
            cards,
            timeline,
            heatmap,
            byUser,
            byArea,
            flagCounts = new
            {
                red = open.Count(f => f.Severity == "red"),
                orange = open.Count(f => f.Severity == "orange"),
                yellow = open.Count(f => f.Severity == "yellow"),
                reviewed = flags.Count - open.Count
            },
            flags = (showReviewed == true ? flags : open)
                .OrderBy(f => Rank(f.Severity)).ThenByDescending(f => f.AtUtc).Take(MaxFlags).ToList()
        });
    }

    private static bool IsMoneyEdit(Row r) =>
        (MoneyTypes.Contains(r.EntityType) && r.Action is "Updated" or "Voided" or "Deleted")
        || (r.EntityType == nameof(FeeAdjustment) && r.Action == "Created")
        || (r.EntityType == nameof(StudentPayment) && r.Action == "Created" && Text(r.Value(nameof(StudentPayment.Kind))) == nameof(PaymentKind.Refund))
        || (r.EntityType == nameof(Student) && r.Action == "Updated" && r.Change(nameof(Student.FeeAmount)) is not null);

    private static async Task<List<Flag>> FlagsAsync(
        AcademyDbContext db, List<Row> rows, List<Row> actions, DateTime fromUtc, decimal alert,
        Func<Row, string> nameOf, Func<Row, string?> roleOf, CancellationToken ct)
    {
        var flags = new List<Flag>();
        string By(Row r) => roleOf(r) is { } role ? $"{nameOf(r)} ({RoleLabel(role)})" : nameOf(r);
        Flag One(Row r, string severity, string title, string detail) => new()
        {
            Key = $"a:{r.Id}", Severity = severity, Title = title, Detail = detail, UserName = By(r),
            AtUtc = r.At, EntityType = r.EntityType, EntityId = r.EntityId
        };
        static List<Guid> Ids(IEnumerable<Row> source) =>
            source.Select(r => Guid.TryParse(r.EntityId, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();

        // ---- Fee payments: cancelled, or their amount changed ----
        var paymentRows = actions.Where(r => r.EntityType == nameof(StudentPayment)
            && (r.Action == "Voided" || r.Action == "Updated" && r.Change(nameof(StudentPayment.Amount)) is not null)).ToList();
        var paymentIds = Ids(paymentRows);
        var payments = await db.StudentPayments.AsNoTracking().Where(p => paymentIds.Contains(p.Id))
            .Select(p => new { p.Id, p.ReceiptNumber, p.Amount, p.Kind, p.VoidReason, Student = p.Student.FullName })
            .ToDictionaryAsync(p => p.Id.ToString(), ct);
        foreach (var r in paymentRows)
        {
            payments.TryGetValue(r.EntityId, out var p);
            var what = p is null ? "A payment" : $"{(p.Kind == PaymentKind.Refund ? "Refund" : "Receipt")} {StudentLedger.ReceiptLabel(p.ReceiptNumber)} of {Money(p.Amount)} for {p.Student}";
            if (r.Action == "Voided")
                flags.Add(One(r, "red", p?.Kind == PaymentKind.Refund ? "Refund cancelled" : "Payment cancelled",
                    $"{what} was cancelled. Reason: {Text(r.Value(nameof(LedgerEntry.VoidReason))) ?? p?.VoidReason ?? "—"}"));
            else if (r.Change(nameof(StudentPayment.Amount)) is var (o, n))
                flags.Add(One(r, "red", "Payment amount changed", $"{what}: {Money(Dec(o))} → {Money(Dec(n))}"));
        }

        // ---- A student's agreed fee lowered ----
        var feeRows = actions.Where(r => r.EntityType == nameof(Student) && r.Action == "Updated"
            && r.Change(nameof(Student.FeeAmount)) is var (o, n) && Dec(n) < Dec(o)).ToList();
        var studentIds = Ids(feeRows);
        // Discounts and write-offs name their student too.
        var adjustmentRows = actions.Where(r => r.EntityType == nameof(FeeAdjustment) && r.Action is "Created" or "Voided").ToList();
        var adjustmentIds = Ids(adjustmentRows.Where(r => r.Action == "Voided"));
        var voidedAdjustments = await db.FeeAdjustments.AsNoTracking().Where(a => adjustmentIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Kind, a.Amount, a.StudentId }).ToDictionaryAsync(a => a.Id.ToString(), ct);
        studentIds.AddRange(adjustmentRows.Select(r => Guid.TryParse(Text(r.Value(nameof(FeeAdjustment.StudentId))), out var g) ? g : Guid.Empty));
        studentIds.AddRange(voidedAdjustments.Values.Select(a => a.StudentId));
        studentIds = studentIds.Where(g => g != Guid.Empty).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName }).ToDictionaryAsync(s => s.Id.ToString(), s => s.FullName, ct);
        string StudentName(string? id) => id is not null && students.TryGetValue(id, out var name) ? name : "a student";

        foreach (var r in feeRows)
        {
            var (o, n) = r.Change(nameof(Student.FeeAmount))!.Value;
            flags.Add(One(r, "red", "Agreed fee lowered", $"{StudentName(r.EntityId)}: {Money(Dec(o))} → {Money(Dec(n))}"));
        }

        // ---- Discounts and write-offs ----
        foreach (var r in adjustmentRows)
        {
            if (r.Action == "Voided")
            {
                voidedAdjustments.TryGetValue(r.EntityId, out var a);
                flags.Add(One(r, "orange", a?.Kind == AdjustmentKind.Surcharge ? "Extra charge cancelled" : "Discount cancelled",
                    a is null ? "A fee adjustment was cancelled." : $"{a.Kind} of {Money(a.Amount)} for {StudentName(a.StudentId.ToString())} was cancelled."));
                continue;
            }
            var kind = Text(r.Value(nameof(FeeAdjustment.Kind)));
            var amount = Dec(r.Value(nameof(FeeAdjustment.Amount)));
            var who = StudentName(Text(r.Value(nameof(FeeAdjustment.StudentId))));
            var reason = Text(r.Value(nameof(FeeAdjustment.Reason)));
            var because = string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason}";
            if (kind == nameof(AdjustmentKind.WriteOff))
                flags.Add(One(r, amount >= alert ? "red" : "orange", "Balance written off",
                    $"{Money(amount)} written off for {who} in a final settlement.{because}"));
            else if (kind == nameof(AdjustmentKind.Discount) && amount >= alert)
                flags.Add(One(r, "red", "Large discount", $"{Money(amount)} discount for {who}.{because}"));
            else if (kind == nameof(AdjustmentKind.Discount) && roleOf(r) != Roles.SuperAdmin)
                flags.Add(One(r, "orange", "Discount given", $"{Money(amount)} discount for {who}.{because}"));
        }

        // ---- Expenses cancelled, and bills removed from expenses ----
        var expenseRows = actions.Where(r => r.EntityType == nameof(Expense) && r.Action == "Voided").ToList();
        var proofRows = actions.Where(r => r.EntityType == nameof(ExpenseProof) && r.Action == "Deleted").ToList();
        var expenseIds = Ids(expenseRows);
        expenseIds.AddRange(proofRows.Select(r => Guid.TryParse(Text(r.Value(nameof(ExpenseProof.ExpenseId))), out var g) ? g : Guid.Empty));
        var expenses = await db.Expenses.AsNoTracking().Where(e => expenseIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Title, e.Amount, e.CreatedAtUtc }).ToDictionaryAsync(e => e.Id.ToString(), ct);
        foreach (var r in expenseRows)
        {
            expenses.TryGetValue(r.EntityId, out var e);
            var days = e is null ? 0 : (int)(r.At - e.CreatedAtUtc).TotalDays;
            var when = days switch { 0 => "the same day it was recorded", 1 => "a day after it was recorded", _ => $"{days} days after it was recorded" };
            flags.Add(One(r, "orange", "Expense cancelled",
                $"{e?.Title ?? "An expense"} ({Money(e?.Amount)}) was cancelled {when}. Reason: {Text(r.Value(nameof(LedgerEntry.VoidReason))) ?? "—"}"));
        }
        foreach (var r in proofRows)
        {
            var expenseId = Text(r.Value(nameof(ExpenseProof.ExpenseId)));
            expenses.TryGetValue(expenseId ?? "", out var e);
            flags.Add(new Flag
            {
                Key = $"a:{r.Id}", Severity = "orange", Title = "Bill removed from an expense",
                Detail = $"{Text(r.Value(nameof(ExpenseProof.FileName))) ?? "A file"} was removed from {e?.Title ?? "an expense"} ({Money(e?.Amount)}).",
                UserName = By(r), AtUtc = r.At, EntityType = nameof(Expense), EntityId = expenseId
            });
        }

        // ---- Faculty payments cancelled ----
        var payoutRows = actions.Where(r => r.EntityType == nameof(FacultyPayout) && r.Action == "Voided").ToList();
        var payoutIds = Ids(payoutRows);
        var payouts = await db.FacultyPayouts.AsNoTracking().Where(p => payoutIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Amount, Faculty = p.Faculty.FullName }).ToDictionaryAsync(p => p.Id.ToString(), ct);
        foreach (var r in payoutRows)
        {
            payouts.TryGetValue(r.EntityId, out var p);
            flags.Add(One(r, "orange", "Faculty payment cancelled",
                $"{Money(p?.Amount)} paid to {p?.Faculty ?? "a faculty member"} was cancelled. Reason: {Text(r.Value(nameof(LedgerEntry.VoidReason))) ?? "—"}"));
        }

        // ---- Faculty classes: times edited, or a typed-in class approved ----
        var sessionRows = actions.Where(r => r.EntityType == nameof(FacultySession) && r.Action == "Updated").ToList();
        var edited = sessionRows.Where(r => r.Change(nameof(FacultySession.Minutes)) is not null
            || r.Change(nameof(FacultySession.StartTime)) is not null || r.Change(nameof(FacultySession.EndTime)) is not null).ToList();
        var approved = sessionRows.Where(r => r.Change(nameof(FacultySession.Review)) is var (o, n)
            && Text(o) == nameof(SessionReview.Pending) && Text(n) == nameof(SessionReview.Approved)).ToList();
        var sessionIds = Ids(edited.Concat(approved));
        var sessions = await db.FacultySessions.AsNoTracking().Where(s => sessionIds.Contains(s.Id))
            .Select(s => new { s.Id, s.TaughtOn, s.Minutes, Faculty = s.Faculty.FullName }).ToDictionaryAsync(s => s.Id.ToString(), ct);
        var typedIn = (await db.FacultyClocks.AsNoTracking()
                .Where(c => c.SessionId != null && sessionIds.Contains(c.SessionId.Value) && (c.InMethod == ClockMethod.Manual || c.CorrectedEndUtc != null))
                .Select(c => c.SessionId!.Value).ToListAsync(ct))
            .Select(id => id.ToString()).ToHashSet();
        string Class(string id) => sessions.TryGetValue(id, out var s)
            ? $"{s.Faculty}'s class on {s.TaughtOn.ToString("dd MMM", CultureInfo.InvariantCulture)} ({Duration(s.Minutes)})" : "A class";
        foreach (var r in edited)
        {
            var change = r.Change(nameof(FacultySession.Minutes)) is var (o, n) ? $": {Duration((int)(Dec(o) ?? 0))} → {Duration((int)(Dec(n) ?? 0))}" : "";
            flags.Add(One(r, "yellow", "Class time edited", $"{Class(r.EntityId)}{change}"));
        }
        foreach (var r in approved.Where(r => typedIn.Contains(r.EntityId) && !edited.Contains(r)))
            flags.Add(One(r, "yellow", "Typed-in class approved", $"{Class(r.EntityId)} was entered or corrected by hand, then approved."));

        // ---- Many deletions or cancellations in a short time, by one person ----
        foreach (var user in actions.Where(r => r.IsRemoval).GroupBy(r => r.UserId?.ToString() ?? r.UserName))
        {
            var list = user.OrderBy(r => r.At).ToList();
            for (var i = 0; i < list.Count;)
            {
                var j = i;
                while (j + 1 < list.Count && list[j + 1].At - list[i].At <= BurstWindow) j++;
                var count = j - i + 1;
                if (count >= BurstSize)
                {
                    flags.Add(new Flag
                    {
                        Key = $"burst:{list[i].Id}", Severity = "orange", Title = "Many deletions at once",
                        Detail = $"{count} records deleted or cancelled between {Time(list[i].Local)} and {Time(list[j].Local)}.",
                        UserName = By(list[i]), AtUtc = list[i].At
                    });
                }
                i = j + 1;
            }
        }

        // ---- Staff working at night. Only the academy's own staff: portal logins clocking out late are normal,
        // the platform owner setting up an academy is not the academy at work, and changing one's own password or
        // profile at night is not a risk. ----
        foreach (var night in actions.Where(r => r.AfterHours && roleOf(r) is { } role && Roles.Tenant.Contains(role)
                                                 && !(r.EntityType == nameof(AppUser) && r.EntityId == r.UserId?.ToString()))
                     .GroupBy(r => (User: r.UserId?.ToString() ?? r.UserName, Night: DateOnly.FromDateTime(r.Local.Hour < NightTo ? r.Local.AddDays(-1) : r.Local))))
        {
            var list = night.OrderBy(r => r.At).ToList();
            var first = list[0];
            var last = list[^1];
            flags.Add(new Flag
            {
                Key = $"night:{night.Key.User}:{night.Key.Night:yyyy-MM-dd}", Severity = "orange", Title = "Working after hours",
                Detail = list.Count == 1
                    ? $"1 change at {Time(first.Local)}, outside 6 AM – 10 PM."
                    : $"{list.Count} changes between {Time(first.Local)} and {Time(last.Local)}, outside 6 AM – 10 PM.",
                UserName = By(first), AtUtc = first.At
            });
        }

        // ---- Sign-in from a network this person has not used before ----
        var logins = rows.Where(r => r.Action == Login && r.UserId is not null && !string.IsNullOrEmpty(r.Ip)).ToList();
        var loginUsers = logins.Select(r => r.UserId!.Value).Distinct().ToList();
        var earlier = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == Login && a.CreatedAtUtc < fromUtc && a.UserId != null && loginUsers.Contains(a.UserId.Value) && a.IpAddress != null)
            .Select(a => new { UserId = a.UserId!.Value, a.IpAddress }).Distinct().ToListAsync(ct);
        var seen = earlier.GroupBy(e => e.UserId).ToDictionary(g => g.Key, g => g.Select(e => e.IpAddress!).ToHashSet());
        foreach (var r in logins)
        {
            var id = r.UserId!.Value;
            if (!seen.TryGetValue(id, out var ips))
            {
                seen[id] = [r.Ip!]; // their first sign-in ever: nothing to compare with
                continue;
            }
            if (ips.Add(r.Ip!))
                flags.Add(One(r, "orange", "Sign-in from a new network", $"Signed in from {r.Ip}, an address not used by this account before."));
        }

        // ---- Repeated failed sign-ins (someone guessing a password) ----
        foreach (var day in rows.Where(r => r.Action == LoginFailed)
                     .GroupBy(r => (User: r.UserId?.ToString() ?? r.UserName, Day: DateOnly.FromDateTime(r.Local))))
        {
            var list = day.OrderBy(r => r.At).ToList();
            if (list.Count < FailedAttemptsFlag) continue;
            var networks = list.Select(r => r.Ip).Where(ip => !string.IsNullOrEmpty(ip)).Distinct().ToList();
            flags.Add(new Flag
            {
                Key = $"fail:{day.Key.User}:{day.Key.Day:yyyy-MM-dd}", Severity = list.Count >= 10 ? "orange" : "yellow",
                Title = "Repeated failed sign-ins",
                Detail = $"{list.Count} wrong passwords between {Time(list[0].Local)} and {Time(list[^1].Local)}"
                         + (networks.Count > 0 ? $" from {string.Join(", ", networks.Take(3))}." : "."),
                UserName = By(list[0]), AtUtc = list[0].At, EntityType = nameof(AppUser), EntityId = list[0].EntityId
            });
        }

        return flags;
    }

    // ---- Record history ----

    /// <summary>Rows that belong to a record: a student's payments and discounts, an expense's bills, a faculty member's classes and pay.</summary>
    private static readonly Dictionary<string, (string Type, string Field)[]> Children = new()
    {
        [nameof(Student)] = [(nameof(StudentPayment), nameof(StudentPayment.StudentId)), (nameof(FeeAdjustment), nameof(FeeAdjustment.StudentId)),
            (nameof(TestResult), nameof(TestResult.StudentId))],
        [nameof(Expense)] = [(nameof(ExpenseProof), nameof(ExpenseProof.ExpenseId))],
        [nameof(Faculty)] = [(nameof(FacultyPayout), nameof(FacultyPayout.FacultyId)), (nameof(FacultySession), nameof(FacultySession.FacultyId))]
    };

    private static async Task<IResult> History(string? entityType, string? entityId, AcademyDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId)) return ApiResults.Bad("Choose a record.");
        var type = entityType.Trim();
        var id = Guid.TryParse(entityId, out var guid) ? guid.ToString() : entityId.Trim();

        var childTypes = new List<string>();
        var childIds = new List<string>();
        foreach (var (childType, field) in Children.GetValueOrDefault(type) ?? [])
        {
            var contains = JsonSerializer.Serialize(new Dictionary<string, string> { [field] = id });
            childTypes.Add(childType);
            childIds.AddRange(await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == childType && a.Action == "Created" && EF.Functions.JsonContains(a.Changes!, contains))
                .Select(a => a.EntityId).ToListAsync(ct));
        }

        var entries = await db.AuditLogs.AsNoTracking()
            .Where(a => (a.EntityType == type && a.EntityId == id) || (childTypes.Contains(a.EntityType) && childIds.Contains(a.EntityId)))
            .OrderByDescending(a => a.Id).Take(500)
            .Select(a => new { a.Id, a.UserName, a.Action, a.EntityType, a.EntityId, a.Changes, a.CreatedAtUtc })
            .ToListAsync(ct);

        return Results.Ok(new { entityType = type, entityId = id, title = await TitleAsync(db, type, guid, ct), entries });
    }

    private static async Task<string?> TitleAsync(AcademyDbContext db, string type, Guid id, CancellationToken ct) => type switch
    {
        nameof(Student) => await db.Students.Where(s => s.Id == id).Select(s => s.FullName).FirstOrDefaultAsync(ct),
        nameof(Faculty) => await db.Faculty.Where(f => f.Id == id).Select(f => f.FullName).FirstOrDefaultAsync(ct),
        nameof(Expense) => await db.Expenses.Where(e => e.Id == id).Select(e => e.Title).FirstOrDefaultAsync(ct),
        nameof(AppUser) => await db.Users.Where(u => u.Id == id).Select(u => u.DisplayName).FirstOrDefaultAsync(ct),
        nameof(StudentPayment) => (await db.StudentPayments.Where(p => p.Id == id)
            .Select(p => new { p.ReceiptNumber, p.Student.FullName }).FirstOrDefaultAsync(ct)) is { } p
            ? $"{StudentLedger.ReceiptLabel(p.ReceiptNumber)} · {p.FullName}" : null,
        _ => null
    };

    // ---- Reviewing flags and the alert amount ----

    private static async Task<IResult> Review(ReviewRequest request, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var key = (request.Key ?? "").Trim();
        if (key.Length is 0 or > 160) return ApiResults.Bad("Choose a flag.");
        var existing = await db.AuditFlagReviews.FirstOrDefaultAsync(r => r.Key == key, ct);
        if (request.Reviewed && existing is null)
        {
            db.AuditFlagReviews.Add(new AuditFlagReview
            {
                Key = key, ReviewedByUserId = currentUser.UserId, ReviewedByName = currentUser.UserName, ReviewedAtUtc = DateTime.UtcNow
            });
        }
        else if (!request.Reviewed && existing is not null)
        {
            db.AuditFlagReviews.Remove(existing);
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { key, reviewed = request.Reviewed });
    }

    private static async Task<IResult> SaveSettings(SettingsRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (!Validate.Amount(request.LargeAmountAlert)) return ApiResults.Bad("Enter a positive amount with at most two decimals.");
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == db.CurrentTenantId, ct);
        if (tenant is null) return Results.NotFound();
        tenant.LargeAmountAlert = request.LargeAmountAlert;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { largeAmountAlert = tenant.LargeAmountAlert });
    }

    // ---- Formatting ----

    private static string? Text(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.String } s => s.GetString(),
        { ValueKind: JsonValueKind.Number } n => n.GetRawText(),
        _ => null
    };

    private static decimal? Dec(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.Number } n when n.TryGetDecimal(out var d) => d,
        { ValueKind: JsonValueKind.String } s when decimal.TryParse(s.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
        _ => null
    };

    private static string Money(decimal? amount) =>
        amount is { } a ? "₹" + a.ToString("#,##,##0.##", CultureInfo.InvariantCulture) : "₹—";

    private static string Time(DateTime local) => local.ToString("dd MMM, h:mm tt", CultureInfo.InvariantCulture);

    private static string Duration(int minutes) => minutes >= 60 ? $"{minutes / 60} h {minutes % 60} min" : $"{minutes} min";

    private static string RoleLabel(string role) => role switch
    {
        Roles.SuperAdmin => "Super Admin",
        _ => role
    };
}
