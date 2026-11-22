using System.Globalization;
using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Billing;
using Academy.Api.Infrastructure.Usage;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// The platform owner's analytics: business growth, each academy's health, which features are used, when the platform
/// is busy (and how fast it answers), and how much storage, email and hosting each academy takes.
/// Everything here is counts, times and totals per academy; no student, fee or record of an academy is shown.
/// </summary>
public static class PlatformAnalytics
{
    public sealed record SettingsRequest(decimal? MonthlyHostingCost, int? DatabaseLimitMb, int? EmailDailyLimit);

    private const string HostingCostKey = "monthlyHostingCost";
    private const string DatabaseLimitKey = "databaseLimitMb";
    private const string EmailLimitKey = "emailDailyLimit";
    /// <summary>Render's smallest paid PostgreSQL and Resend's free tier, until the owner says otherwise.</summary>
    private const int DefaultDatabaseLimitMb = 1024;
    private const int DefaultEmailDailyLimit = 100;

    private static readonly string[] PortalRoles = [Roles.Faculty, Roles.Student];

    public static void MapPlatformAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/analytics").RequireAuthorization(Policies.Owner);
        group.MapGet("/business", Business);
        group.MapGet("/health", Health);
        group.MapGet("/usage", Usage);
        group.MapGet("/load", Load);
        group.MapGet("/capacity", Capacity);
        group.MapPut("/settings", SaveSettings);
    }

    private static DateOnly TodayIst => AcademyClock.LocalDate(DateTime.UtcNow);
    private static DateOnly MonthStart(DateOnly d) => new(d.Year, d.Month, 1);
    private static string MonthLabel(DateOnly d) => d.ToString("MMM yyyy", CultureInfo.InvariantCulture);

    private static (DateOnly From, DateOnly To, DateTime FromUtc, DateTime ToUtc) Range(DateOnly? from, DateOnly? to, int defaultDays = 30)
    {
        var end = to ?? TodayIst;
        var start = from ?? end.AddDays(-(defaultDays - 1));
        if (start > end) (start, end) = (end, start);
        return (start, end, AcademyClock.UtcStartOf(start), AcademyClock.UtcStartOf(end.AddDays(1)));
    }

    // ---------------------------------------------------------------- 1. Business growth

    private static async Task<IResult> Business(int? months, AcademyDbContext db, CancellationToken ct)
    {
        var count = Math.Clamp(months ?? 12, 3, 36);
        var today = TodayIst;
        var first = MonthStart(today).AddMonths(-(count - 1));

        var tenants = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Select(t => new { t.Id, t.Name, t.Code, t.Status, t.CreatedAtUtc, t.SuspendedAtUtc, t.SuspensionReason }).ToListAsync(ct);
        var subscriptions = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Select(s => new { s.TenantId, s.Status, s.Price, s.Interval, s.CurrentPeriodEnd, s.UpdatedAtUtc, PlanName = s.Plan.Name, s.Plan.MaxStudents, s.Plan.MaxUsers })
            .ToListAsync(ct);
        var invoices = await db.SubscriptionInvoices.IgnoreQueryFilters().AsNoTracking().Where(i => i.Status != InvoiceStatus.Void)
            .Select(i => new { i.TenantId, i.Amount, i.PeriodStart, i.PeriodEnd, i.Status, i.PaidAtUtc, i.DueOn, i.InvoiceNumber }).ToListAsync(ct);
        var names = tenants.ToDictionary(t => t.Id, t => t.Name);
        var subOf = subscriptions.GroupBy(s => s.TenantId).ToDictionary(g => g.Key, g => g.First());

        // Each invoice's amount spread evenly over the months it pays for: the revenue each month really earned.
        var earned = new Dictionary<(Guid Tenant, DateOnly Month), decimal>();
        foreach (var i in invoices)
        {
            var monthsCovered = new List<DateOnly>();
            for (var m = MonthStart(i.PeriodStart); m <= i.PeriodEnd; m = m.AddMonths(1)) monthsCovered.Add(m);
            if (monthsCovered.Count == 0) continue;
            var share = i.Amount / monthsCovered.Count;
            foreach (var m in monthsCovered) earned[(i.TenantId, m)] = earned.GetValueOrDefault((i.TenantId, m)) + share;
        }
        decimal Earned(Guid tenant, DateOnly month) => earned.GetValueOrDefault((tenant, month));

        var series = new List<object>();
        for (var m = first; m <= MonthStart(today); m = m.AddMonths(1))
        {
            var startUtc = AcademyClock.UtcStartOf(m);
            var endUtc = AcademyClock.UtcStartOf(m.AddMonths(1));
            decimal newMrr = 0, expansion = 0, contraction = 0, churned = 0;
            foreach (var t in tenants)
            {
                var now = Earned(t.Id, m);
                var before = Earned(t.Id, m.AddMonths(-1));
                if (before == 0 && now > 0) newMrr += now;
                else if (now > before && before > 0) expansion += now - before;
                else if (now < before && now > 0) contraction += before - now;
                else if (before > 0 && now == 0) churned += before;
            }
            series.Add(new
            {
                month = m,
                label = MonthLabel(m),
                mrr = decimal.Round(tenants.Sum(t => Earned(t.Id, m)), 2),
                collected = invoices.Where(i => i.Status == InvoiceStatus.Paid && i.PaidAtUtc >= startUtc && i.PaidAtUtc < endUtc).Sum(i => i.Amount),
                newMrr = decimal.Round(newMrr, 2),
                expansion = decimal.Round(expansion, 2),
                contraction = decimal.Round(contraction, 2),
                churned = decimal.Round(churned, 2),
                newAcademies = tenants.Count(t => t.CreatedAtUtc >= startUtc && t.CreatedAtUtc < endUtc),
                lostAcademies = tenants.Count(t => t.SuspendedAtUtc >= startUtc && t.SuspendedAtUtc < endUtc)
                                + subscriptions.Count(s => s.Status == SubscriptionStatus.Cancelled && s.UpdatedAtUtc >= startUtc && s.UpdatedAtUtc < endUtc
                                                           && names.ContainsKey(s.TenantId) && tenants.First(t => t.Id == s.TenantId).Status == TenantStatus.Active)
            });
        }

        // Current state of every academy.
        string StateOf(Guid id, TenantStatus status) => status == TenantStatus.Suspended ? "Suspended"
            : subOf.TryGetValue(id, out var s) ? s.Status switch
            {
                SubscriptionStatus.Trialing => "Trial",
                SubscriptionStatus.Cancelled => "Cancelled",
                SubscriptionStatus.PastDue => "Past due",
                _ => "Paying"
            } : "No plan";
        var states = tenants.Select(t => StateOf(t.Id, t.Status)).ToList();

        // Trials: who went on to pay, and how long it took.
        var firstPaid = invoices.Where(i => i.Status == InvoiceStatus.Paid && i.Amount > 0 && i.PaidAtUtc != null)
            .GroupBy(i => i.TenantId).ToDictionary(g => g.Key, g => g.Min(i => i.PaidAtUtc!.Value));
        var decided = tenants.Where(t => StateOf(t.Id, t.Status) != "Trial").ToList();
        var converted = decided.Where(t => firstPaid.ContainsKey(t.Id)).ToList();
        var daysToPay = converted.Select(t => (firstPaid[t.Id] - t.CreatedAtUtc).TotalDays).ToList();

        var paying = subscriptions.Where(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue
                                              && tenants.Any(t => t.Id == s.TenantId && t.Status == TenantStatus.Active)).ToList();
        var byPlan = subscriptions.Where(s => s.Status != SubscriptionStatus.Cancelled)
            .GroupBy(s => s.PlanName)
            .Select(g => new
            {
                plan = g.Key,
                academies = g.Count(),
                paying = g.Count(s => paying.Contains(s)),
                trial = g.Count(s => s.Status == SubscriptionStatus.Trialing),
                mrr = decimal.Round(g.Where(s => paying.Contains(s)).Sum(s => s.Price / s.Interval.Months()), 2)
            })
            .OrderByDescending(p => p.mrr).ThenByDescending(p => p.academies).ToList();

        var renewals = subscriptions
            .Where(s => s.Status != SubscriptionStatus.Cancelled && s.CurrentPeriodEnd >= today && s.CurrentPeriodEnd <= today.AddDays(30)
                        && tenants.Any(t => t.Id == s.TenantId && t.Status == TenantStatus.Active))
            .OrderBy(s => s.CurrentPeriodEnd)
            .Select(s => new { tenantId = s.TenantId, academy = names.GetValueOrDefault(s.TenantId, "?"), plan = s.PlanName, s.Price, status = s.Status.ToString(), renewsOn = s.CurrentPeriodEnd.AddDays(1) })
            .ToList();
        var overdue = invoices.Where(i => i.Status == InvoiceStatus.Open && i.DueOn < today)
            .OrderBy(i => i.DueOn)
            .Select(i => new { tenantId = i.TenantId, academy = names.GetValueOrDefault(i.TenantId, "?"), i.InvoiceNumber, i.Amount, i.DueOn, daysOverdue = today.DayNumber - i.DueOn.DayNumber })
            .ToList();

        // Upgrade opportunities: at 80% or more of a plan limit. Portal logins do not count towards the user limit.
        var students = await db.Students.IgnoreQueryFilters().Where(s => s.Active).GroupBy(s => s.TenantId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var staff = await db.Users.IgnoreQueryFilters().Where(u => u.TenantId != null && u.Active && !PortalRoles.Contains(u.Role))
            .GroupBy(u => u.TenantId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var upgrades = new List<object>();
        foreach (var t in tenants.Where(t => t.Status == TenantStatus.Active && subOf.ContainsKey(t.Id)))
        {
            var s = subOf[t.Id];
            var studentUse = s.MaxStudents is > 0 ? (double)students.GetValueOrDefault(t.Id) / s.MaxStudents.Value : 0;
            var userUse = s.MaxUsers is > 0 ? (double)staff.GetValueOrDefault(t.Id) / s.MaxUsers.Value : 0;
            if (studentUse >= 0.8 || userUse >= 0.8)
                upgrades.Add(new
                {
                    tenantId = t.Id, academy = t.Name, plan = s.PlanName,
                    students = students.GetValueOrDefault(t.Id), maxStudents = s.MaxStudents,
                    users = staff.GetValueOrDefault(t.Id), maxUsers = s.MaxUsers,
                    usePercent = (int)Math.Round(Math.Max(studentUse, userUse) * 100)
                });
        }

        var thisMonth = MonthStart(today);
        return Results.Ok(new
        {
            months = series,
            currentMrr = decimal.Round(paying.Sum(s => s.Price / s.Interval.Months()), 2),
            earnedThisMonth = decimal.Round(tenants.Sum(t => Earned(t.Id, thisMonth)), 2),
            academies = new
            {
                total = tenants.Count,
                paying = states.Count(s => s == "Paying"),
                trial = states.Count(s => s == "Trial"),
                pastDue = states.Count(s => s == "Past due"),
                suspended = states.Count(s => s == "Suspended"),
                cancelled = states.Count(s => s == "Cancelled"),
                newThisMonth = tenants.Count(t => AcademyClock.LocalDate(t.CreatedAtUtc) >= thisMonth)
            },
            trials = new
            {
                decided = decided.Count,
                converted = converted.Count,
                conversionPercent = decided.Count == 0 ? 0 : (int)Math.Round(100.0 * converted.Count / decided.Count),
                averageDaysToPay = daysToPay.Count == 0 ? (double?)null : Math.Round(daysToPay.Average(), 1)
            },
            lost = tenants.Where(t => t.Status == TenantStatus.Suspended)
                .OrderByDescending(t => t.SuspendedAtUtc)
                .Select(t => new { tenantId = t.Id, academy = t.Name, on = t.SuspendedAtUtc, reason = t.SuspensionReason ?? "" })
                .Concat(subscriptions.Where(s => s.Status == SubscriptionStatus.Cancelled && tenants.Any(t => t.Id == s.TenantId && t.Status == TenantStatus.Active))
                    .Select(s => new { tenantId = s.TenantId, academy = names.GetValueOrDefault(s.TenantId, "?"), on = (DateTime?)s.UpdatedAtUtc, reason = "Subscription cancelled" }))
                .ToList(),
            byPlan,
            popularPlan = byPlan.OrderByDescending(p => p.academies).FirstOrDefault()?.plan,
            renewals,
            overdue,
            upgrades
        });
    }

    // ---------------------------------------------------------------- 2. Academy health

    private static async Task<IResult> Health(AcademyDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var since14 = now.AddDays(-14);
        var since30 = now.AddDays(-30);

        var tenants = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Select(t => new { t.Id, t.Name, t.Code, t.Status, t.CreatedAtUtc, t.LogoVersion }).ToListAsync(ct);
        var subs = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Select(s => new { s.TenantId, s.Status, PlanName = s.Plan.Name, s.CurrentPeriodEnd })
            .ToDictionaryAsync(s => s.TenantId, ct);

        var users = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.TenantId != null)
            .Select(u => new { TenantId = u.TenantId!.Value, u.Role, u.Active, u.LastLoginAtUtc }).ToListAsync(ct);
        // A wrong password is not use of the system, and the owner's own changes to an academy are not the academy's.
        var lastAudit = await db.AuditLogs.IgnoreQueryFilters().Where(a => a.TenantId != null && a.Action != "LoginFailed"
                                                                           && db.Users.IgnoreQueryFilters().Any(u => u.Id == a.UserId && u.TenantId == a.TenantId))
            .GroupBy(a => a.TenantId!.Value).Select(g => new { g.Key, Last = g.Max(a => a.CreatedAtUtc) })
            .ToDictionaryAsync(g => g.Key, g => g.Last, ct);
        var lastUsage = await db.UsageHours.Where(u => u.TenantId != Guid.Empty)
            .GroupBy(u => u.TenantId).Select(g => new { g.Key, Last = g.Max(u => u.HourUtc) })
            .ToDictionaryAsync(g => g.Key, g => g.Last, ct);

        // Days with activity in the last 14: sign-ins in the log, plus any hour with requests.
        var loginDays = (await db.AuditLogs.IgnoreQueryFilters().Where(a => a.TenantId != null && a.Action == "Login" && a.CreatedAtUtc >= since14)
                .Select(a => new { TenantId = a.TenantId!.Value, a.CreatedAtUtc }).ToListAsync(ct))
            .Select(a => (a.TenantId, Day: AcademyClock.LocalDate(a.CreatedAtUtc)));
        var usageDays = (await db.UsageUserHours.Where(u => u.HourUtc >= since14).Select(u => new { u.TenantId, u.HourUtc }).Distinct().ToListAsync(ct))
            .Select(u => (u.TenantId, Day: AcademyClock.LocalDate(u.HourUtc)));
        var activeDays = loginDays.Concat(usageDays).Distinct().GroupBy(d => d.TenantId).ToDictionary(g => g.Key, g => g.Count());
        var features14 = await db.UsageHours.Where(u => u.HourUtc >= since14 && u.TenantId != Guid.Empty
                                                        && u.Feature != "Sign-in" && u.Feature != "Account & password" && u.Feature != "Session check"
                                                        && u.Feature != "Dashboard")
            .GroupBy(u => u.TenantId).Select(g => new { g.Key, Count = g.Select(u => u.Feature).Distinct().Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        async Task<Dictionary<Guid, int>> CountBy<T>(IQueryable<T> rows) where T : TenantEntity =>
            await rows.GroupBy(r => r.TenantId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        var students = await CountBy(db.Students.IgnoreQueryFilters());
        var activeStudents = await CountBy(db.Students.IgnoreQueryFilters().Where(s => s.Active));
        var students14 = await CountBy(db.Students.IgnoreQueryFilters().Where(s => s.CreatedAtUtc >= since14));
        var courses = await CountBy(db.Courses.IgnoreQueryFilters());
        var faculty = await CountBy(db.Faculty.IgnoreQueryFilters());
        var expenses = await CountBy(db.Expenses.IgnoreQueryFilters());
        var posted = db.StudentPayments.IgnoreQueryFilters().Where(p => p.Status == LedgerStatus.Posted && p.Kind == PaymentKind.Payment);
        var fees = await posted.GroupBy(p => p.TenantId)
            .Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(p => p.Amount) }).ToDictionaryAsync(g => g.Key, ct);
        var fees30 = await posted.Where(p => p.CreatedAtUtc >= since30).GroupBy(p => p.TenantId)
            .Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(p => p.Amount) }).ToDictionaryAsync(g => g.Key, ct);
        var fees14 = await CountBy(posted.Where(p => p.CreatedAtUtc >= since14));

        var rows = tenants.Select(t =>
        {
            var tUsers = users.Where(u => u.TenantId == t.Id).ToList();
            var candidates = new[] { tUsers.Max(u => u.LastLoginAtUtc), lastAudit.TryGetValue(t.Id, out var a) ? a : (DateTime?)null, lastUsage.TryGetValue(t.Id, out var h) ? h : (DateTime?)null };
            var lastActive = candidates.Where(d => d is not null).DefaultIfEmpty(null).Max();
            var idleDays = lastActive is { } la ? (int)(now - la).TotalDays : (int)(now - t.CreatedAtUtc).TotalDays;

            var days = activeDays.GetValueOrDefault(t.Id);
            var featureCount = features14.GetValueOrDefault(t.Id);
            var score = Math.Min(40, days * 4) + (fees14.GetValueOrDefault(t.Id) > 0 ? 20 : 0)
                        + (students14.GetValueOrDefault(t.Id) > 0 ? 10 : 0) + Math.Min(30, featureCount * 3);
            var health = t.Status == TenantStatus.Suspended ? "suspended"
                : idleDays >= 10 ? "quiet"
                : idleDays >= 5 || score < 40 ? "slowing"
                : "active";

            var steps = new[]
            {
                new { step = "Logo set", done = t.LogoVersion is not null },
                new { step = "Course added", done = courses.GetValueOrDefault(t.Id) > 0 },
                new { step = "First student", done = students.GetValueOrDefault(t.Id) > 0 },
                new { step = "First fee recorded", done = fees.ContainsKey(t.Id) },
                new { step = "Faculty added", done = faculty.GetValueOrDefault(t.Id) > 0 },
                new { step = "Portal logins given", done = tUsers.Any(u => PortalRoles.Contains(u.Role)) }
            };
            subs.TryGetValue(t.Id, out var sub);
            return new
            {
                tenantId = t.Id, academy = t.Name, t.Code, createdAtUtc = t.CreatedAtUtc,
                plan = sub?.PlanName, subscription = sub?.Status.ToString(), periodEnd = sub?.CurrentPeriodEnd,
                health, score, lastActiveUtc = lastActive, idleDays, activeDays14 = days, features14 = featureCount,
                lastByRole = tUsers.Where(u => u.LastLoginAtUtc != null).GroupBy(u => u.Role)
                    .Select(g => new { role = g.Key, lastUtc = g.Max(u => u.LastLoginAtUtc) }).OrderByDescending(r => r.lastUtc).ToList(),
                onboarding = steps,
                onboardingDone = steps.Count(s => s.done),
                totals = new
                {
                    students = students.GetValueOrDefault(t.Id),
                    activeStudents = activeStudents.GetValueOrDefault(t.Id),
                    faculty = faculty.GetValueOrDefault(t.Id),
                    staffUsers = tUsers.Count(u => u.Active && !PortalRoles.Contains(u.Role)),
                    portalUsers = tUsers.Count(u => u.Active && PortalRoles.Contains(u.Role)),
                    feesRecorded = fees.TryGetValue(t.Id, out var f) ? f.Count : 0,
                    feesTotal = f?.Total ?? 0,
                    fees30 = fees30.TryGetValue(t.Id, out var f30) ? f30.Total : 0,
                    expenses = expenses.GetValueOrDefault(t.Id)
                }
            };
        })
        .OrderBy(r => r.health switch { "quiet" => 0, "slowing" => 1, "active" => 2, _ => 3 }).ThenBy(r => r.score).ToList();

        return Results.Ok(new
        {
            counts = new
            {
                active = rows.Count(r => r.health == "active"),
                slowing = rows.Count(r => r.health == "slowing"),
                quiet = rows.Count(r => r.health == "quiet"),
                suspended = rows.Count(r => r.health == "suspended")
            },
            platformTotals = new
            {
                students = students.Values.Sum(),
                activeStudents = activeStudents.Values.Sum(),
                faculty = faculty.Values.Sum(),
                staffUsers = rows.Sum(r => r.totals.staffUsers),
                portalUsers = rows.Sum(r => r.totals.portalUsers),
                feesRecorded = fees.Values.Sum(f => f.Count),
                feesTotal = fees.Values.Sum(f => f.Total)
            },
            onboardingFunnel = rows.Count == 0 ? [] : rows[0].onboarding.Select((s, i) => new
            {
                s.step,
                academies = rows.Count(r => r.onboarding[i].done),
                percent = (int)Math.Round(100.0 * rows.Count(r => r.onboarding[i].done) / rows.Count)
            }).ToList(),
            academies = rows
        });
    }

    // ---------------------------------------------------------------- 3. Feature usage

    private static async Task<IResult> Usage(DateOnly? from, DateOnly? to, AcademyDbContext db, CancellationToken ct)
    {
        var (start, end, fromUtc, toUtc) = Range(from, to);
        var rows = await db.UsageHours.AsNoTracking().Where(u => u.HourUtc >= fromUtc && u.HourUtc < toUtc).ToListAsync(ct);
        // Features academies chose to use; session checks and sign-ins are load, not features.
        var academyRows = rows.Where(r => r.TenantId != Guid.Empty && !UsageFeatures.Background.Contains(r.Feature)).ToList();
        var activeAcademies = await db.Tenants.IgnoreQueryFilters().CountAsync(t => t.Status == TenantStatus.Active, ct);
        var trackingSince = await db.UsageHours.MinAsync(u => (DateTime?)u.HourUtc, ct);

        var features = academyRows.GroupBy(r => r.Feature).ToDictionary(g => g.Key, g => g.ToList());
        var names = UsageFeatures.AcademyFeatures.Union(features.Keys).ToList();
        var ranking = names.Select(name =>
        {
            var list = features.GetValueOrDefault(name) ?? [];
            var requests = list.Sum(r => r.Requests);
            return new
            {
                feature = name,
                requests,
                academies = list.Select(r => r.TenantId).Distinct().Count(),
                adoptionPercent = activeAcademies == 0 ? 0 : (int)Math.Round(100.0 * list.Select(r => r.TenantId).Distinct().Count() / activeAcademies),
                avgMs = requests == 0 ? 0 : (int)(list.Sum(r => r.TotalMs) / requests),
                mobilePercent = requests == 0 ? 0 : (int)Math.Round(100.0 * list.Where(r => r.Device == "Mobile").Sum(r => r.Requests) / requests),
                byRole = list.GroupBy(r => r.Role).ToDictionary(g => g.Key, g => g.Sum(r => r.Requests))
            };
        }).OrderByDescending(f => f.requests).ToList();

        // Last eight weeks per feature, whatever range is chosen: shows how a new feature is being taken up.
        var weekStart = ReportPeriods.StartOf(TodayIst, ReportGrouping.Week).AddDays(-7 * 7);
        var weeklyRows = await db.UsageHours.AsNoTracking()
            .Where(u => u.HourUtc >= AcademyClock.UtcStartOf(weekStart) && u.TenantId != Guid.Empty
                        && u.Feature != "Session check" && u.Feature != "Sign-in" && u.Feature != "Account & password")
            .GroupBy(u => new { u.Feature, u.HourUtc }).Select(g => new { g.Key.Feature, g.Key.HourUtc, Requests = g.Sum(u => u.Requests) })
            .ToListAsync(ct);
        var weeks = Enumerable.Range(0, 8).Select(i => weekStart.AddDays(7 * i)).ToList();
        var weekly = weeklyRows.GroupBy(r => r.Feature).ToDictionary(
            g => g.Key,
            g => weeks.Select(w => g.Where(r => ReportPeriods.StartOf(AcademyClock.LocalDate(r.HourUtc), ReportGrouping.Week) == w).Sum(r => r.Requests)).ToList());

        // Portal adoption: logins given vs logins that used the portal in the range.
        var portalUsers = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Active && PortalRoles.Contains(u.Role))
            .Select(u => new { u.Id, u.Role }).ToListAsync(ct);
        var portalIds = portalUsers.Select(u => u.Id).ToList();
        var seen = (await db.UsageUserHours.Where(u => u.HourUtc >= fromUtc && u.HourUtc < toUtc && portalIds.Contains(u.UserId))
            .Select(u => u.UserId).Distinct().ToListAsync(ct)).ToHashSet();
        object Adoption(string role) => new
        {
            given = portalUsers.Count(u => u.Role == role),
            used = portalUsers.Count(u => u.Role == role && seen.Contains(u.Id))
        };

        var activeUsers = await db.UsageUserHours.Where(u => u.HourUtc >= fromUtc && u.HourUtc < toUtc && u.TenantId != Guid.Empty)
            .Select(u => u.UserId).Distinct().CountAsync(ct);
        var total = academyRows.Sum(r => r.Requests);
        var mobile = academyRows.Where(r => r.Device == "Mobile").Sum(r => r.Requests);

        return Results.Ok(new
        {
            from = start, to = end, trackingSince,
            totals = new
            {
                requests = total,
                academiesUsing = academyRows.Select(r => r.TenantId).Distinct().Count(),
                activeAcademies,
                activeUsers,
                mobilePercent = total == 0 ? 0 : (int)Math.Round(100.0 * mobile / total)
            },
            features = ranking,
            unused = ranking.Where(f => f.requests == 0 && UsageFeatures.AcademyFeatures.Contains(f.feature)).Select(f => f.feature).ToList(),
            roles = academyRows.GroupBy(r => r.Role).Select(g => new
            {
                role = g.Key,
                requests = g.Sum(r => r.Requests),
                mobilePercent = g.Sum(r => r.Requests) == 0 ? 0 : (int)Math.Round(100.0 * g.Where(r => r.Device == "Mobile").Sum(r => r.Requests) / g.Sum(r => r.Requests)),
                topFeatures = g.GroupBy(r => r.Feature).OrderByDescending(f => f.Sum(r => r.Requests)).Take(5)
                    .Select(f => new { feature = f.Key, requests = f.Sum(r => r.Requests) }).ToList()
            }).OrderByDescending(r => r.requests).ToList(),
            weeks = weeks.Select(w => w.ToString("dd MMM", CultureInfo.InvariantCulture)).ToList(),
            weekly,
            portals = new { faculty = Adoption(Roles.Faculty), students = Adoption(Roles.Student) },
            devices = new { mobile, desktop = total - mobile }
        });
    }

    // ---------------------------------------------------------------- 4. Peak hours, speed and errors

    private static int Percentile(IEnumerable<UsageHour> rows, double p)
    {
        int b100 = 0, b300 = 0, b1 = 0, b3 = 0, over = 0, max = 0;
        foreach (var r in rows) { b100 += r.Under100Ms; b300 += r.Under300Ms; b1 += r.Under1s; b3 += r.Under3s; over += r.Over3s; max = Math.Max(max, r.MaxMs); }
        var total = b100 + b300 + b1 + b3 + over;
        if (total == 0) return 0;
        var target = total * p;
        // Upper edge of the bucket the percentile falls in ("95% of requests answered within…"), never above the slowest seen.
        if (b100 >= target) return Math.Min(100, max);
        if (b100 + b300 >= target) return Math.Min(300, max);
        if (b100 + b300 + b1 >= target) return Math.Min(1000, max);
        if (b100 + b300 + b1 + b3 >= target) return Math.Min(3000, max);
        return max;
    }

    private static async Task<IResult> Load(DateOnly? from, DateOnly? to, Guid? tenantId, AcademyDbContext db, CancellationToken ct)
    {
        var (start, end, fromUtc, toUtc) = Range(from, to);
        var query = db.UsageHours.AsNoTracking().Where(u => u.HourUtc >= fromUtc && u.HourUtc < toUtc);
        if (tenantId is { } only) query = query.Where(u => u.TenantId == only);
        var rows = await query.ToListAsync(ct);
        var names = await db.Tenants.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var heatmap = Enumerable.Range(0, 7).Select(_ => new int[24]).ToArray();
        var byHour = new int[24];
        foreach (var r in rows)
        {
            var local = AcademyClock.ToLocal(r.HourUtc);
            heatmap[((int)local.DayOfWeek + 6) % 7][local.Hour] += r.Requests;
            byHour[local.Hour] += r.Requests;
        }
        var peakHour = byHour.Any(h => h > 0) ? Array.IndexOf(byHour, byHour.Max()) : (int?)null;
        // The quietest three hours in a row (wrapping past midnight): the window for deploys and maintenance.
        int? quietStart = null;
        if (rows.Count > 0)
        {
            var best = int.MaxValue;
            for (var h = 0; h < 24; h++)
            {
                var sum = byHour[h] + byHour[(h + 1) % 24] + byHour[(h + 2) % 24];
                if (sum < best) { best = sum; quietStart = h; }
            }
        }

        var peaks = tenantId is null
            ? await db.UsagePeakHours.AsNoTracking().Where(p => p.HourUtc >= fromUtc && p.HourUtc < toUtc).OrderByDescending(p => p.PeakPerMinute).FirstOrDefaultAsync(ct)
            : null;
        var userHours = db.UsageUserHours.Where(u => u.HourUtc >= fromUtc && u.HourUtc < toUtc);
        if (tenantId is { } t2) userHours = userHours.Where(u => u.TenantId == t2);
        var online = await userHours.GroupBy(u => u.HourUtc).Select(g => new { Hour = g.Key, Users = g.Count() })
            .OrderByDescending(g => g.Users).FirstOrDefaultAsync(ct);

        var total = rows.Sum(r => r.Requests);
        var byFeature = rows.GroupBy(r => r.Feature).Select(g => new
        {
            feature = g.Key,
            requests = g.Sum(r => r.Requests),
            avgMs = (int)(g.Sum(r => r.TotalMs) / Math.Max(1, g.Sum(r => r.Requests))),
            p95Ms = Percentile(g, 0.95),
            maxMs = g.Max(r => r.MaxMs),
            serverErrors = g.Sum(r => r.ServerErrors),
            clientErrors = g.Sum(r => r.ClientErrors)
        }).ToList();

        var days = ReportPeriods.Split(start, end, ReportGrouping.Day);
        var daily = days.Select(d =>
        {
            var inDay = rows.Where(r => AcademyClock.LocalDate(r.HourUtc) == d.From).ToList();
            var n = inDay.Sum(r => r.Requests);
            return new
            {
                date = d.From,
                requests = n,
                serverErrors = inDay.Sum(r => r.ServerErrors),
                avgMs = n == 0 ? 0 : (int)(inDay.Sum(r => r.TotalMs) / n),
                p95Ms = Percentile(inDay, 0.95)
            };
        }).ToList();

        var perAcademy = rows.Where(r => r.TenantId != Guid.Empty).GroupBy(r => r.TenantId).Select(g =>
        {
            var hours = new int[24];
            foreach (var r in g) hours[AcademyClock.ToLocal(r.HourUtc).Hour] += r.Requests;
            var n = g.Sum(r => r.Requests);
            return new
            {
                tenantId = g.Key,
                academy = names.GetValueOrDefault(g.Key, "?"),
                requests = n,
                sharePercent = total == 0 ? 0 : Math.Round(100.0 * n / total, 1),
                peakHour = Array.IndexOf(hours, hours.Max()),
                lateNightPercent = n == 0 ? 0 : (int)Math.Round(100.0 * (hours[22] + hours[23] + hours[0] + hours[1] + hours[2] + hours[3] + hours[4] + hours[5]) / n)
            };
        }).OrderByDescending(a => a.requests).ToList();

        // Background work in the same days.
        var emails = await db.EmailMessages.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.CreatedAtUtc >= fromUtc && m.CreatedAtUtc < toUtc && (tenantId == null || m.TenantId == tenantId))
            .Select(m => new { m.Status, m.Category }).ToListAsync(ct);
        var invoicesRaised = await db.SubscriptionInvoices.IgnoreQueryFilters()
            .CountAsync(i => i.CreatedAtUtc >= fromUtc && i.CreatedAtUtc < toUtc && (tenantId == null || i.TenantId == tenantId), ct);
        var autoClosed = await db.FacultyClocks.IgnoreQueryFilters()
            .CountAsync(c => c.OutMethod == ClockMethod.Auto && c.OutAtUtc >= fromUtc && c.OutAtUtc < toUtc && (tenantId == null || c.TenantId == tenantId), ct);
        var reminders = await db.FacultyClocks.IgnoreQueryFilters()
            .CountAsync(c => c.ReminderSentAtUtc >= fromUtc && c.ReminderSentAtUtc < toUtc && (tenantId == null || c.TenantId == tenantId), ct);

        return Results.Ok(new
        {
            from = start, to = end, tenantId,
            academy = tenantId is { } id ? names.GetValueOrDefault(id) : null,
            heatmap,
            byHour,
            peakHour,
            quietStart,
            peakPerMinute = peaks?.PeakPerMinute ?? 0,
            peakMinuteUtc = peaks?.PeakMinuteUtc,
            mostOnline = online?.Users ?? 0,
            mostOnlineHourUtc = online?.Hour,
            totals = new
            {
                requests = total,
                avgMs = total == 0 ? 0 : (int)(rows.Sum(r => r.TotalMs) / total),
                p50Ms = Percentile(rows, 0.5),
                p95Ms = Percentile(rows, 0.95),
                maxMs = rows.Count == 0 ? 0 : rows.Max(r => r.MaxMs),
                slowRequests = rows.Sum(r => r.Under3s + r.Over3s),
                serverErrors = rows.Sum(r => r.ServerErrors),
                clientErrors = rows.Sum(r => r.ClientErrors),
                errorPercent = total == 0 ? 0 : Math.Round(100.0 * rows.Sum(r => r.ServerErrors) / total, 2)
            },
            slowest = byFeature.Where(f => f.requests >= 5).OrderByDescending(f => f.p95Ms).ThenByDescending(f => f.avgMs).Take(10).ToList(),
            errors = byFeature.Where(f => f.serverErrors > 0 || f.clientErrors > 0).OrderByDescending(f => f.serverErrors).ThenByDescending(f => f.clientErrors).Take(10).ToList(),
            daily,
            perAcademy,
            jobs = new
            {
                emailsSent = emails.Count(m => m.Status == EmailStatus.Sent),
                emailsFailed = emails.Count(m => m.Status == EmailStatus.Failed),
                emailsWaiting = emails.Count(m => m.Status == EmailStatus.Pending),
                emailsByType = emails.GroupBy(m => m.Category).Select(g => new { category = g.Key, count = g.Count() }).OrderByDescending(g => g.count).ToList(),
                invoicesRaised,
                classesAutoClosed = autoClosed,
                classRemindersSent = reminders
            }
        });
    }

    // ---------------------------------------------------------------- 5. Capacity and cost

    private sealed class TenantCount
    {
        public Guid? TenantId { get; set; }
        public long Value { get; set; }
    }

    private static async Task<Dictionary<string, string>> SettingsAsync(AcademyDbContext db, CancellationToken ct) =>
        await db.PlatformSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);

    private static async Task<IResult> Capacity(AcademyDbContext db, CancellationToken ct)
    {
        var settings = await SettingsAsync(db, ct);
        var hostingCost = decimal.TryParse(settings.GetValueOrDefault(HostingCostKey), NumberStyles.Number, CultureInfo.InvariantCulture, out var hc) ? hc : 0;
        var dbLimitMb = int.TryParse(settings.GetValueOrDefault(DatabaseLimitKey), out var dl) ? dl : DefaultDatabaseLimitMb;
        var emailLimit = int.TryParse(settings.GetValueOrDefault(EmailLimitKey), out var el) ? el : DefaultEmailDailyLimit;

        var dbBytes = await db.Database.SqlQueryRaw<long>("select pg_database_size(current_database()) as \"Value\"").SingleAsync(ct);
        var names = await db.Tenants.IgnoreQueryFilters().AsNoTracking().Select(t => new { t.Id, t.Name, t.Status }).ToListAsync(ct);

        // Every table that records an academy, with its size and how much of it belongs to each academy.
        var tables = db.Model.GetEntityTypes()
            .Where(e => e.FindProperty("TenantId") is not null && e.GetTableName() is not null)
            .Select(e => (Table: e.GetTableName()!, HasCreated: e.FindProperty("CreatedAtUtc") is not null, HasData: e.FindProperty("Data") is not null))
            .DistinctBy(t => t.Table).ToList();
        var perTenant = new Dictionary<Guid, double>();
        var tableSizes = new List<(string Table, long Bytes, long Rows)>();
        double growth30 = 0;
        foreach (var (table, hasCreated, hasData) in tables)
        {
            var size = await db.Database.SqlQueryRaw<long>($"select pg_total_relation_size('{table}') as \"Value\"").SingleAsync(ct);
            // Files are weighed by their bytes; everything else by its number of rows.
            var weights = await db.Database.SqlQueryRaw<TenantCount>(hasData
                ? $"select tenant_id as \"TenantId\", coalesce(sum(octet_length(data)), 0)::bigint as \"Value\" from {table} group by tenant_id"
                : $"select tenant_id as \"TenantId\", count(*)::bigint as \"Value\" from {table} group by tenant_id").ToListAsync(ct);
            var rows = await db.Database.SqlQueryRaw<long>($"select count(*)::bigint as \"Value\" from {table}").SingleAsync(ct);
            tableSizes.Add((table, size, rows));
            var totalWeight = weights.Sum(w => w.Value);
            if (totalWeight > 0)
                foreach (var w in weights.Where(w => w.TenantId is { } id && id != Guid.Empty))
                    perTenant[w.TenantId!.Value] = perTenant.GetValueOrDefault(w.TenantId!.Value) + (double)size * w.Value / totalWeight;
            if (hasCreated && rows > 0)
            {
                var recent = await db.Database.SqlQueryRaw<long>(hasData
                    ? $"select coalesce(sum(octet_length(data)), 0)::bigint as \"Value\" from {table} where created_at_utc >= now() - interval '30 days'"
                    : $"select count(*)::bigint as \"Value\" from {table} where created_at_utc >= now() - interval '30 days'").SingleAsync(ct);
                growth30 += hasData ? (double)size * recent / Math.Max(1, totalWeight) : (double)size * recent / rows;
            }
        }

        // Measured growth from the daily size snapshots, once there are enough of them.
        var snapshots = await db.DatabaseSizeDays.AsNoTracking().OrderBy(d => d.Date).ToListAsync(ct);
        var monthAgo = snapshots.LastOrDefault(s => s.Date <= TodayIst.AddDays(-28));
        double? measuredGrowth = monthAgo is not null ? (dbBytes - monthAgo.Bytes) * 30.0 / Math.Max(1, TodayIst.DayNumber - monthAgo.Date.DayNumber) : null;
        var monthlyGrowth = Math.Max(0, measuredGrowth ?? growth30);
        var limitBytes = dbLimitMb * 1024L * 1024L;
        double? monthsLeft = monthlyGrowth > 0 ? Math.Round((limitBytes - dbBytes) / monthlyGrowth, 1) : null;

        // Email: per academy in the last 30 days, and the busiest day against the provider's daily limit.
        var since30 = DateTime.UtcNow.AddDays(-30);
        var mails = await db.EmailMessages.IgnoreQueryFilters().AsNoTracking().Where(m => m.CreatedAtUtc >= since30)
            .Select(m => new { m.TenantId, m.CreatedAtUtc, m.Status }).ToListAsync(ct);
        var today = TodayIst;
        var mailsToday = mails.Count(m => AcademyClock.LocalDate(m.CreatedAtUtc) == today);
        var busiestDay = mails.GroupBy(m => AcademyClock.LocalDate(m.CreatedAtUtc)).Select(g => new { date = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).FirstOrDefault();

        // Cost share: half by requests (server time), half by storage. Revenue is the academy's monthly subscription value.
        var requests = await db.UsageHours.Where(u => u.HourUtc >= since30 && u.TenantId != Guid.Empty)
            .GroupBy(u => u.TenantId).Select(g => new { g.Key, Requests = g.Sum(u => u.Requests) }).ToDictionaryAsync(g => g.Key, g => g.Requests, ct);
        var totalRequests = Math.Max(1, requests.Values.Sum());
        var totalStored = Math.Max(1, perTenant.Values.Sum());
        var mrr = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)
            .Select(s => new { s.TenantId, s.Price, s.Interval }).ToListAsync(ct);
        var revenue = mrr.GroupBy(s => s.TenantId).ToDictionary(g => g.Key, g => g.Sum(s => s.Price / s.Interval.Months()));

        var fileBytes = await db.Database.SqlQueryRaw<TenantCount>(
            "select tenant_id as \"TenantId\", coalesce(sum(octet_length(data)), 0)::bigint as \"Value\" from expense_proofs group by tenant_id").ToListAsync(ct);
        var filesByTenant = fileBytes.Where(f => f.TenantId is not null).ToDictionary(f => f.TenantId!.Value, f => f.Value);

        var academies = names.Select(t =>
        {
            var stored = perTenant.GetValueOrDefault(t.Id);
            var share = 0.5 * requests.GetValueOrDefault(t.Id) / totalRequests + 0.5 * stored / totalStored;
            var cost = decimal.Round(hostingCost * (decimal)share, 2);
            var income = decimal.Round(revenue.GetValueOrDefault(t.Id), 2);
            return new
            {
                tenantId = t.Id, academy = t.Name, status = t.Status.ToString(),
                storageBytes = (long)stored,
                filesBytes = filesByTenant.GetValueOrDefault(t.Id),
                emails30 = mails.Count(m => m.TenantId == t.Id),
                emailsFailed30 = mails.Count(m => m.TenantId == t.Id && m.Status == EmailStatus.Failed),
                requests30 = requests.GetValueOrDefault(t.Id),
                costSharePercent = Math.Round(share * 100, 1),
                cost,
                revenue = income,
                margin = income - cost
            };
        }).OrderByDescending(a => a.storageBytes).ToList();

        return Results.Ok(new
        {
            settings = new { monthlyHostingCost = hostingCost, databaseLimitMb = dbLimitMb, emailDailyLimit = emailLimit },
            database = new
            {
                bytes = dbBytes,
                limitBytes,
                usedPercent = Math.Round(100.0 * dbBytes / limitBytes, 1),
                monthlyGrowthBytes = (long)monthlyGrowth,
                growthMeasured = measuredGrowth is not null,
                monthsLeft,
                filesBytes = filesByTenant.Values.Sum(),
                tables = tableSizes.OrderByDescending(t => t.Bytes).Take(12).Select(t => new { table = t.Table, bytes = t.Bytes, rows = t.Rows }).ToList(),
                history = snapshots.TakeLast(90).Select(s => new { date = s.Date, bytes = s.Bytes }).ToList()
            },
            email = new
            {
                today = mailsToday,
                dailyLimit = emailLimit,
                todayPercent = emailLimit == 0 ? 0 : (int)Math.Round(100.0 * mailsToday / emailLimit),
                busiestDay = busiestDay?.date,
                busiestDayCount = busiestDay?.count ?? 0,
                last30 = mails.Count,
                failed30 = mails.Count(m => m.Status == EmailStatus.Failed)
            },
            hosting = new
            {
                monthlyCost = hostingCost,
                monthlyRevenue = decimal.Round(revenue.Values.Sum(), 2),
                costPerAcademy = names.Count(n => n.Status == TenantStatus.Active) is var active and > 0 ? decimal.Round(hostingCost / active, 2) : 0
            },
            academies
        });
    }

    private static async Task<IResult> SaveSettings(SettingsRequest request, AcademyDbContext db, CancellationToken ct)
    {
        if (request.MonthlyHostingCost is < 0 or > 10_000_000) return ApiResults.Bad("Enter a monthly hosting cost of zero or more.");
        if (request.DatabaseLimitMb is < 100 or > 10_000_000) return ApiResults.Bad("Enter the database limit in MB (at least 100).");
        if (request.EmailDailyLimit is < 1 or > 10_000_000) return ApiResults.Bad("Enter the daily email limit (at least 1).");

        async Task Set(string key, string? value)
        {
            if (value is null) return;
            var row = await db.PlatformSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (row is null) db.PlatformSettings.Add(row = new PlatformSetting { Key = key });
            row.Value = value;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        await Set(HostingCostKey, request.MonthlyHostingCost?.ToString(CultureInfo.InvariantCulture));
        await Set(DatabaseLimitKey, request.DatabaseLimitMb?.ToString(CultureInfo.InvariantCulture));
        await Set(EmailLimitKey, request.EmailDailyLimit?.ToString(CultureInfo.InvariantCulture));
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }
}
