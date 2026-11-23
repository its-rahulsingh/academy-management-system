using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using Academy.Api.Data;
using Academy.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Infrastructure.Usage;

/// <summary>
/// One hour of requests to one feature, from one academy, role and kind of device. The owner's analytics read these.
/// Only counts and timings are kept: no user, no record, no amount. Rows are hourly totals, so the table stays small.
/// </summary>
public sealed class UsageHour
{
    /// <summary>The hour, in UTC, truncated.</summary>
    public DateTime HourUtc { get; set; }
    /// <summary><see cref="Guid.Empty"/> for requests outside any academy: the owner console, sign-in pages, anonymous.</summary>
    public Guid TenantId { get; set; }
    public string Feature { get; set; } = "";
    /// <summary>The signed-in role, or "Visitor".</summary>
    public string Role { get; set; } = "";
    /// <summary>"Mobile" or "Desktop", from the browser's user agent.</summary>
    public string Device { get; set; } = "";
    public int Requests { get; set; }
    /// <summary>Requests the server failed (5xx).</summary>
    public int ServerErrors { get; set; }
    /// <summary>Requests refused as invalid or not allowed (4xx).</summary>
    public int ClientErrors { get; set; }
    public long TotalMs { get; set; }
    public int MaxMs { get; set; }
    /// <summary>Response time buckets, for typical and slowest (95th percentile) times: under 100 ms, 300 ms, 1 s, 3 s, and 3 s or more.</summary>
    public int Under100Ms { get; set; }
    public int Under300Ms { get; set; }
    public int Under1s { get; set; }
    public int Under3s { get; set; }
    public int Over3s { get; set; }
}

/// <summary>Which signed-in accounts made requests in an hour: for "people online" and active users. Ids only.</summary>
public sealed class UsageUserHour
{
    public DateTime HourUtc { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
}

/// <summary>The database's size once a day, so the owner sees how fast it grows.</summary>
public sealed class DatabaseSizeDay
{
    public DateOnly Date { get; set; }
    public long Bytes { get; set; }
}

/// <summary>The busiest minute of each hour across the whole platform.</summary>
public sealed class UsagePeakHour
{
    public DateTime HourUtc { get; set; }
    public int PeakPerMinute { get; set; }
    public DateTime PeakMinuteUtc { get; set; }
}

/// <summary>
/// Collects request counts in memory and writes them to the database about once a minute, adding to the hour's totals.
/// A request costs a dictionary update; nothing waits for the database.
/// </summary>
public sealed class UsageTracker
{
    /// <summary>An endpoint can name the feature more precisely than its route (e.g. QR vs location clock-in).</summary>
    public const string FeatureKey = "usage.feature";

    private sealed class Counter
    {
        public int Requests, ServerErrors, ClientErrors, MaxMs, Under100, Under300, Under1s, Under3s, Over3s;
        public long TotalMs;
    }

    private readonly record struct Key(DateTime Hour, Guid TenantId, string Feature, string Role, string Device);

    private ConcurrentDictionary<Key, Counter> _counters = new();
    private ConcurrentDictionary<(DateTime Hour, Guid UserId), Guid> _users = new();
    private ConcurrentDictionary<DateTime, int> _minutes = new();

    public static void Tag(HttpContext http, string feature) => http.Items[FeatureKey] = feature;

    /// <summary>
    /// The start of the Indian-time hour containing this moment, as UTC (e.g. 10:00–11:00 IST is stored as 04:30 UTC).
    /// Hours line up with the clock academies work by, so "busiest hour 10–11 AM" means exactly that.
    /// </summary>
    public static DateTime HourStart(DateTime utc)
    {
        var local = utc.AddMinutes(330);
        return new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Utc).AddMinutes(-330);
    }

    /// <summary>Records today's database size (Indian date), once a day.</summary>
    public static async Task SnapshotDatabaseSizeAsync(AcademyDbContext db, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into database_size_days (date, bytes) select {today}, pg_database_size(current_database())
            on conflict (date) do update set bytes = excluded.bytes
            """, ct);
    }

    public void Record(DateTime utcNow, Guid tenantId, string feature, string role, string device, int status, int ms, Guid? userId)
    {
        var hour = HourStart(utcNow);
        var c = _counters.GetOrAdd(new Key(hour, tenantId, feature, role, device), _ => new Counter());
        lock (c)
        {
            c.Requests++;
            if (status >= 500) c.ServerErrors++;
            else if (status >= 400) c.ClientErrors++;
            c.TotalMs += ms;
            if (ms > c.MaxMs) c.MaxMs = ms;
            if (ms < 100) c.Under100++;
            else if (ms < 300) c.Under300++;
            else if (ms < 1000) c.Under1s++;
            else if (ms < 3000) c.Under3s++;
            else c.Over3s++;
        }
        if (userId is { } id) _users.TryAdd((hour, id), tenantId);
        var minute = hour.AddMinutes(utcNow.Minute);
        _minutes.AddOrUpdate(minute, 1, (_, n) => n + 1);
    }

    /// <summary>Adds what was collected since the last flush to the hourly rows.</summary>
    /// <summary>Saves the counts gathered since the last flush. Returns false (without touching the database) when there were none.</summary>
    public async Task<bool> FlushAsync(AcademyDbContext db, CancellationToken ct)
    {
        var counters = Interlocked.Exchange(ref _counters, new());
        var users = Interlocked.Exchange(ref _users, new());
        var minutes = Interlocked.Exchange(ref _minutes, new());
        if (counters.IsEmpty && users.IsEmpty && minutes.IsEmpty) return false;

        foreach (var (k, c) in counters)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                insert into usage_hours (hour_utc, tenant_id, feature, role, device, requests, server_errors, client_errors, total_ms, max_ms,
                    under100_ms, under300_ms, under1s, under3s, over3s)
                values ({k.Hour}, {k.TenantId}, {k.Feature}, {k.Role}, {k.Device}, {c.Requests}, {c.ServerErrors}, {c.ClientErrors}, {c.TotalMs}, {c.MaxMs},
                    {c.Under100}, {c.Under300}, {c.Under1s}, {c.Under3s}, {c.Over3s})
                on conflict (hour_utc, tenant_id, feature, role, device) do update set
                    requests = usage_hours.requests + excluded.requests,
                    server_errors = usage_hours.server_errors + excluded.server_errors,
                    client_errors = usage_hours.client_errors + excluded.client_errors,
                    total_ms = usage_hours.total_ms + excluded.total_ms,
                    max_ms = greatest(usage_hours.max_ms, excluded.max_ms),
                    under100_ms = usage_hours.under100_ms + excluded.under100_ms,
                    under300_ms = usage_hours.under300_ms + excluded.under300_ms,
                    under1s = usage_hours.under1s + excluded.under1s,
                    under3s = usage_hours.under3s + excluded.under3s,
                    over3s = usage_hours.over3s + excluded.over3s
                """, ct);
        }
        foreach (var ((hour, userId), tenantId) in users)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                insert into usage_user_hours (hour_utc, user_id, tenant_id) values ({hour}, {userId}, {tenantId})
                on conflict do nothing
                """, ct);
        }
        // A minute can straddle two flushes; its two halves are compared, not added. Close enough for a peak.
        foreach (var hour in minutes.GroupBy(m => HourStart(m.Key)))
        {
            var top = hour.MaxBy(m => m.Value);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                insert into usage_peak_hours (hour_utc, peak_per_minute, peak_minute_utc) values ({hour.Key}, {top.Value}, {top.Key})
                on conflict (hour_utc) do update set
                    peak_minute_utc = case when excluded.peak_per_minute > usage_peak_hours.peak_per_minute then excluded.peak_minute_utc else usage_peak_hours.peak_minute_utc end,
                    peak_per_minute = greatest(usage_peak_hours.peak_per_minute, excluded.peak_per_minute)
                """, ct);
        }
        return true;
    }
}

/// <summary>Times every API request and hands it to the <see cref="UsageTracker"/>.</summary>
public sealed class UsageMiddleware(RequestDelegate next, UsageTracker tracker)
{
    public async Task InvokeAsync(HttpContext http)
    {
        var path = http.Request.Path;
        if (!path.StartsWithSegments("/api") || path.StartsWithSegments("/api/health") || HttpMethods.IsOptions(http.Request.Method))
        {
            await next(http);
            return;
        }

        var watch = Stopwatch.StartNew();
        var status = 500;
        try
        {
            await next(http);
            status = http.Response.StatusCode;
        }
        finally
        {
            watch.Stop();
            var user = http.User;
            var role = user.FindFirstValue(ClaimTypes.Role) ?? "Visitor";
            var tenant = Guid.TryParse(user.FindFirstValue(AppClaims.TenantId), out var t) ? t : Guid.Empty;
            var userId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var u) ? u : (Guid?)null;
            var feature = http.Items[UsageTracker.FeatureKey] as string
                          ?? UsageFeatures.For((http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? path.Value ?? "", http.Request.Method);
            var device = (http.Request.Headers.UserAgent.ToString() is var agent && (agent.Contains("Mobi") || agent.Contains("Android"))) ? "Mobile" : "Desktop";
            tracker.Record(DateTime.UtcNow, tenant, feature, role, device, status, (int)Math.Min(watch.ElapsedMilliseconds, int.MaxValue), userId);
        }
    }
}

/// <summary>Writes the collected usage to the database every minute (and once more on shutdown).</summary>
public sealed class UsageFlushService(UsageTracker tracker, IServiceScopeFactory scopes, ILogger<UsageFlushService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) await FlushAsync(CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        await FlushAsync(CancellationToken.None);
    }

    /// <summary>When the database size was last recorded. It only changes when people use the app, so it is
    /// recorded at most once an hour, and only in a minute that had activity (the database is awake then anyway).</summary>
    private DateTime _lastSizeSnapshotUtc = DateTime.MinValue;

    private async Task FlushAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AcademyDbContext>();
            if (!await tracker.FlushAsync(db, ct)) return;
            if (DateTime.UtcNow - _lastSizeSnapshotUtc < TimeSpan.FromHours(1)) return;
            await UsageTracker.SnapshotDatabaseSizeAsync(db, ct);
            _lastSizeSnapshotUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            // Usage numbers are not worth failing anything for; the next minute tries again with new counts.
            logger.LogWarning(ex, "Could not save usage counts");
        }
    }
}

/// <summary>Names a request's feature from its route, in words the owner reads.</summary>
public static class UsageFeatures
{
    /// <summary>Every feature an academy can use, so the owner also sees the ones nobody used.</summary>
    public static readonly IReadOnlyList<string> AcademyFeatures =
    [
        "Dashboard", "Students", "Online admissions", "Courses", "Subjects", "Fee collection", "Receipts", "Discounts & surcharges",
        "Settle balance", "Fee reminders", "Promote to next year", "Expenses", "Expense bills", "Faculty & pay", "Faculty attendance",
        "Class check-in (staff)", "QR code display", "QR clock-in", "Location clock-in", "Forgot-to-clock requests", "Faculty portal",
        "Student portal", "Portal logins", "Tests & marks", "Report cards", "Reports", "Daily report", "Excel downloads",
        "Users", "Activity log", "Activity dashboard", "Academic years", "Branding & email", "Subscription & billing"
    ];

    /// <summary>Housekeeping the app does by itself (checking the session on each page load): counted for load, not as features.</summary>
    public static readonly IReadOnlySet<string> Background = new HashSet<string> { "Session check", "Sign-in", "Account & password" };

    private static readonly (string Prefix, string Feature)[] Rules =
    [
        ("auth/login", "Sign-in"), ("auth/logout", "Sign-in"), ("auth/session", "Session check"), ("auth", "Account & password"),
        ("platform", "Owner console"), ("public/admissions", "Online admissions"), ("public", "Sign-in"),
        ("dashboard", "Dashboard"), ("academic-years", "Academic years"),
        ("courses", "Courses"), ("subjects", "Subjects"),
        ("students/promot", "Promote to next year"), ("students/{id:guid}/settle", "Settle balance"),
        ("students/{id:guid}/adjustments", "Discounts & surcharges"), ("students/adjustments", "Discounts & surcharges"),
        ("students/{id:guid}/login", "Portal logins"), ("students/{id:guid}/reminder", "Fee reminders"), ("students/reminders", "Fee reminders"),
        ("students", "Students"), ("admissions", "Online admissions"),
        ("branding", "Branding & email"),
        ("payments/{id:guid}/receipt", "Receipts"), ("payments", "Fee collection"),
        ("faculty/attendance", "Faculty attendance"), ("faculty/{id:guid}/login", "Portal logins"), ("faculty", "Faculty & pay"),
        ("me/faculty/clock/request", "Forgot-to-clock requests"), ("me/faculty/clock", "Faculty portal"), ("me/faculty", "Faculty portal"),
        ("attendance/qr", "QR code display"), ("attendance", "Class check-in (staff)"),
        ("me/student", "Student portal"),
        ("expenses/{id:guid}/proofs", "Expense bills"), ("expenses", "Expenses"),
        ("tests/report-cards", "Report cards"), ("tests", "Tests & marks"),
        ("reports/activity", "Daily report"), ("reports", "Reports"),
        ("users", "Users"),
        ("audit/dashboard", "Activity dashboard"), ("audit/history", "Activity dashboard"), ("audit/flags", "Activity dashboard"),
        ("audit/settings", "Activity dashboard"), ("audit", "Activity log"),
        ("billing", "Subscription & billing"),
    ];

    public static string For(string route, string method)
    {
        var r = route.TrimStart('/');
        if (r.StartsWith("api/", StringComparison.OrdinalIgnoreCase)) r = r[4..];
        if (!r.StartsWith("platform", StringComparison.OrdinalIgnoreCase)
            && (r.EndsWith("/excel", StringComparison.OrdinalIgnoreCase) || r.Contains("/export", StringComparison.OrdinalIgnoreCase)))
            return "Excel downloads";
        foreach (var (prefix, feature) in Rules)
            if (r.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return feature;
        var first = r.Split('/', 2)[0];
        return first.Length == 0 ? "Other" : char.ToUpperInvariant(first[0]) + first[1..].Replace('-', ' ');
    }
}
