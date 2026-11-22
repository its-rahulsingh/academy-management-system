using System.Globalization;
using System.Security.Cryptography;
using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Email;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Academy.Api.Features;

/// <summary>
/// Short-lived check-in codes. An admin's screen shows a QR code that changes every few seconds; a faculty member
/// scans it to prove they are at the academy. A code names the academy and the admin who showed it, the time it was
/// made, and is signed with the academy's own secret, so it cannot be forged, used at another academy, or reused
/// from a photo once it has expired.
/// </summary>
public static class CheckInCodes
{
    /// <summary>How long a code stays valid after it is shown. The screen shows a new one well before this.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    public const int RefreshSeconds = 20;
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(10);
    private const byte Version = 1;
    private const int PayloadLength = 1 + 16 + 16 + 8;
    private const int SignatureLength = 16;

    public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string Issue(string secret, Guid tenantId, Guid issuerId, DateTimeOffset now)
    {
        var payload = new byte[PayloadLength];
        payload[0] = Version;
        tenantId.TryWriteBytes(payload.AsSpan(1, 16));
        issuerId.TryWriteBytes(payload.AsSpan(17, 16));
        BitConverter.TryWriteBytes(payload.AsSpan(33, 8), now.ToUnixTimeSeconds());
        var signature = HMACSHA256.HashData(Convert.FromHexString(secret), payload).AsSpan(0, SignatureLength);
        return Base64Url([.. payload, .. signature]);
    }

    /// <summary>Returns the admin who showed the code, or an error a faculty member can act on.</summary>
    public static (Guid? IssuerId, string? Error) Verify(string secret, Guid tenantId, string? code, DateTimeOffset now)
    {
        const string invalid = "That is not a check-in code from this academy. Scan the code on the admin's screen.";
        if (string.IsNullOrWhiteSpace(secret) || !TryFromBase64Url(code, out var bytes) || bytes.Length != PayloadLength + SignatureLength || bytes[0] != Version)
            return (null, invalid);

        var payload = bytes.AsSpan(0, PayloadLength);
        var expected = HMACSHA256.HashData(Convert.FromHexString(secret), payload).AsSpan(0, SignatureLength);
        if (!CryptographicOperations.FixedTimeEquals(expected, bytes.AsSpan(PayloadLength, SignatureLength))) return (null, invalid);
        if (new Guid(payload.Slice(1, 16)) != tenantId) return (null, invalid);

        var issued = DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt64(payload.Slice(33, 8)));
        if (issued > now + ClockSkew || now - issued > Lifetime)
            return (null, "That code has expired. Scan the code showing on the admin's screen now.");
        return (new Guid(payload.Slice(17, 16)), null);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryFromBase64Url(string? text, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200) return false;
        var b64 = text.Trim().Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        try
        {
            bytes = Convert.FromBase64String(b64);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class Geo
{
    /// <summary>Great-circle distance in metres.</summary>
    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadius = 6_371_000;
        static double Rad(double degrees) => degrees * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * earthRadius * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}

/// <summary>
/// Clocking in and out. A faculty member clocks in when a class starts and out when it ends; the server's clock gives
/// the minutes. The proof of presence is the admin's QR code by default, or the phone's location when no admin is free.
/// A class clocked in and out by QR, within the academy's maximum class length and with no times typed in by hand,
/// is approved at once; anything else waits for approval.
/// </summary>
public static class AttendanceEndpoints
{
    /// <summary>Phone locations less precise than this are refused rather than guessed at.</summary>
    private const double MaxAccuracyMeters = 500;
    /// <summary>Allowance for the phone's own uncertainty, added to the academy's radius.</summary>
    private const double MaxAccuracyAllowance = 100;
    /// <summary>How far back a forgotten class can still be entered.</summary>
    public const int ManualRequestMaxDaysBack = 31;

    public sealed record ClockInRequest(ClockMethod Method, string? Code, double? Latitude, double? Longitude, double? Accuracy, Guid? SubjectId);
    /// <param name="EndTime">"HH:mm" on the class's date, when the class ended earlier than now. Optional.</param>
    public sealed record ClockOutRequest(ClockMethod Method, string? Code, double? Latitude, double? Longitude, double? Accuracy, string? Topic, string? EndTime = null);
    public sealed record SettingsRequest(double? Latitude, double? Longitude, int RadiusMeters, int? MaxClassMinutes = null, bool? AutoApprovePackageClasses = null);
    public sealed record StopRequest(string? EndTime);
    public sealed record EndTimeRequest(string? EndTime, string? Topic);
    public sealed record ManualRequest(DateOnly? Date, string? StartTime, string? EndTime, Guid? SubjectId, string? Topic, string? Reason);

    public static void MapAttendanceEndpoints(this RouteGroupBuilder academy)
    {
        var staff = academy.MapGroup("/attendance");
        staff.MapGet("/qr", Qr).RequirePermission(Permissions.AttendanceManage);
        staff.MapGet("/live", Live).RequirePermission(Permissions.AttendanceManage);
        staff.MapPost("/{id:guid}/stop", Stop).RequirePermission(Permissions.AttendanceManage);
        staff.MapPost("/{id:guid}/end", StaffEndTime).RequirePermission(Permissions.AttendanceManage);
        staff.MapGet("/settings", Settings).RequirePermission(Permissions.AttendanceManage);
        staff.MapPut("/settings", SaveSettings).RequirePermission(Permissions.BrandingManage);

        var portal = academy.MapGroup("/me/faculty/clock").RequirePermission(Permissions.FacultyPortal);
        portal.MapGet("", Status);
        portal.MapPost("/in", ClockIn);
        portal.MapPost("/out", ClockOut);
        portal.MapPost("/request", Request);
        portal.MapPost("/{id:guid}/end", FacultyEndTime);
    }

    // ---- Time helpers ----

    /// <summary>"17:30" on the given academy date, as UTC.</summary>
    private static DateTime? LocalTimeOn(DateOnly date, string? text) =>
        TimeOnly.TryParseExact((text ?? "").Trim(), ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? DateTime.SpecifyKind(date.ToDateTime(time) - AcademyClock.Offset, DateTimeKind.Utc)
            : null;

    private static string Clock(DateTime utc) => AcademyClock.ToLocal(utc).ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant();

    private static string DayAndClock(DateTime utc) => AcademyClock.ToLocal(utc).ToString("dd MMM, h:mm tt", CultureInfo.InvariantCulture).Replace("AM", "am").Replace("PM", "pm");

    /// <summary>Checks a typed-in end time for a class that started at <paramref name="inAtUtc"/>; returns the UTC end or an error.</summary>
    private static (DateTime? End, IResult? Error) CorrectedEnd(DateTime inAtUtc, string? endTime, DateTime latestUtc)
    {
        var date = AcademyClock.LocalDate(inAtUtc);
        if (LocalTimeOn(date, endTime) is not { } end) return (null, ApiResults.Bad("Enter the time the class ended, e.g. 17:30."));
        if (end <= inAtUtc.AddMinutes(1)) return (null, ApiResults.Bad($"The class started at {Clock(inAtUtc)}, so it must end after that."));
        if (end > latestUtc) return (null, ApiResults.Bad("The end time cannot be later than now."));
        return (end, null);
    }

    private static Task<int> MaxClassMinutesAsync(AcademyDbContext db, Guid tenantId, CancellationToken ct) =>
        db.Tenants.Where(t => t.Id == tenantId).Select(t => t.MaxClassMinutes).FirstAsync(ct);

    // ---- Staff ----

    private static async Task<IResult> Qr(AcademyDbContext db, ICurrentUser currentUser, IOptions<AppOptions> options, HttpContext http, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstAsync(t => t.Id == currentUser.TenantId, ct);
        if (tenant.CheckInSecret.Length == 0)
        {
            tenant.CheckInSecret = CheckInCodes.NewSecret();
            await db.SaveChangesAsync(ct);
        }

        var now = DateTimeOffset.UtcNow;
        var code = CheckInCodes.Issue(tenant.CheckInSecret, tenant.Id, currentUser.UserId!.Value, now);
        return Results.Ok(new
        {
            code,
            // Opening this link (e.g. from the phone's own camera app) checks in or out the signed-in faculty member.
            url = $"{AuthEndpoints.PublicBaseUrl(options.Value, http)}/teacher/scan?code={code}",
            issuedAtUtc = now.UtcDateTime,
            expiresInSeconds = (int)CheckInCodes.Lifetime.TotalSeconds,
            refreshSeconds = CheckInCodes.RefreshSeconds
        });
    }

    private static async Task<IResult> Live(AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var max = await MaxClassMinutesAsync(db, currentUser.TenantId!.Value, ct);
        var running = await db.FacultyClocks.AsNoTracking()
            .Where(c => c.OutAtUtc == null)
            .OrderBy(c => c.InAtUtc)
            .Select(c => new
            {
                c.Id, c.FacultyId, FacultyName = c.Faculty.FullName, SubjectName = c.Subject != null ? c.Subject.Name : null,
                c.InAtUtc, c.InMethod, c.InDistanceMeters, c.ReminderSentAtUtc,
                VerifiedBy = db.Users.Where(u => u.Id == c.InVerifiedByUserId).Select(u => u.DisplayName).FirstOrDefault()
            })
            .ToListAsync(ct);
        var needsEndTime = await db.FacultyClocks.AsNoTracking()
            .Where(c => c.OutMethod == ClockMethod.Auto && c.SessionId == null)
            .OrderBy(c => c.InAtUtc)
            .Select(c => new { c.Id, c.FacultyId, FacultyName = c.Faculty.FullName, SubjectName = c.Subject != null ? c.Subject.Name : null, c.InAtUtc, c.InMethod })
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        return Results.Ok(new
        {
            serverNowUtc = now,
            maxClassMinutes = max,
            items = running.Select(c => new
            {
                c.Id, c.FacultyId, c.FacultyName, c.SubjectName, c.InAtUtc, c.InMethod, c.InDistanceMeters, c.VerifiedBy,
                overdue = (now - c.InAtUtc).TotalMinutes > max,
                reminded = c.ReminderSentAtUtc != null
            }),
            needsEndTime
        });
    }

    private static async Task<IResult> Stop(Guid id, StopRequest? request, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var facultyId = await db.FacultyClocks.Where(c => c.Id == id).Select(c => (Guid?)c.FacultyId).FirstOrDefaultAsync(ct);
        if (facultyId is null) return Results.NotFound();
        return await db.InTransactionAsync<IResult>(async () =>
        {
            if (!await db.LockFacultyAsync(facultyId.Value, ct)) return Results.NotFound();
            var clock = await db.FacultyClocks.FirstAsync(c => c.Id == id, ct);
            if (clock.OutAtUtc is not null) return ApiResults.Conflict("This class has already ended.");

            var now = DateTime.UtcNow;
            DateTime? corrected = null;
            if (!string.IsNullOrWhiteSpace(request?.EndTime))
            {
                var (end, error) = CorrectedEnd(clock.InAtUtc, request.EndTime, now);
                if (error is not null) return error;
                corrected = end;
            }

            clock.OutAtUtc = now;
            clock.OutMethod = ClockMethod.Staff;
            clock.OutVerifiedByUserId = currentUser.UserId;
            clock.CorrectedEndUtc = corrected;
            var note = corrected is { } c
                ? $"Stopped by {currentUser.UserName} at {DayAndClock(now)}; class ended {Clock(c)}"
                : $"Stopped by {currentUser.UserName}";
            return Results.Ok(await FinishAsync(db, clock, "", note, ct));
        }, ct);
    }

    /// <summary>Completes a class the system closed at midnight, once someone knows when it really ended.</summary>
    private static Task<IResult> StaffEndTime(Guid id, EndTimeRequest request, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
        CompleteAutoClosedAsync(db, id, null, request, $"End time entered by {currentUser.UserName}", ct);

    private static async Task<IResult> Settings(AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
        Results.Ok(await db.Tenants.AsNoTracking().Where(t => t.Id == currentUser.TenantId)
            .Select(t => new { t.Latitude, t.Longitude, radiusMeters = t.GeofenceRadiusMeters, enabled = t.Latitude != null, t.MaxClassMinutes, t.AutoApprovePackageClasses })
            .FirstAsync(ct));

    private static async Task<IResult> SaveSettings(SettingsRequest request, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if ((request.Latitude is null) != (request.Longitude is null)) return ApiResults.Bad("Give both latitude and longitude, or neither.");
        if (request.Latitude is { } lat && (lat is < -90 or > 90 || request.Longitude is < -180 or > 180))
            return ApiResults.Bad("That is not a valid location.");
        if (request.RadiusMeters is < 30 or > 5000) return ApiResults.Bad("Choose a radius between 30 and 5,000 metres.");
        if (request.MaxClassMinutes is < 30 or > 720) return ApiResults.Bad("Choose a maximum class length between 30 minutes and 12 hours.");

        var tenant = await db.Tenants.FirstAsync(t => t.Id == currentUser.TenantId, ct);
        tenant.Latitude = request.Latitude is { } la ? Math.Round(la, 6) : null;
        tenant.Longitude = request.Longitude is { } lo ? Math.Round(lo, 6) : null;
        tenant.GeofenceRadiusMeters = request.RadiusMeters;
        if (request.MaxClassMinutes is { } max) tenant.MaxClassMinutes = max;
        if (request.AutoApprovePackageClasses is { } auto) tenant.AutoApprovePackageClasses = auto;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new
        {
            tenant.Latitude, tenant.Longitude, radiusMeters = tenant.GeofenceRadiusMeters, enabled = tenant.Latitude != null, tenant.MaxClassMinutes,
            tenant.AutoApprovePackageClasses
        });
    }

    // ---- Faculty portal ----

    private static async Task<Faculty?> CurrentFacultyAsync(AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var facultyId = await db.Users.Where(u => u.Id == currentUser.UserId).Select(u => u.FacultyId).FirstOrDefaultAsync(ct);
        return facultyId is null ? null : await db.Faculty.AsNoTracking().FirstOrDefaultAsync(f => f.Id == facultyId, ct);
    }

    private static IResult NotLinked() =>
        ApiResults.Error(StatusCodes.Status403Forbidden, "This login is not linked to a faculty record. Ask the academy to check it.");

    private static async Task<IResult> Status(AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (await CurrentFacultyAsync(db, currentUser, ct) is not { } faculty) return NotLinked();
        var tenant = await db.Tenants.AsNoTracking().Where(t => t.Id == currentUser.TenantId)
            .Select(t => new { t.Latitude, t.GeofenceRadiusMeters, t.MaxClassMinutes }).FirstAsync(ct);
        var running = await db.FacultyClocks.AsNoTracking()
            .Where(c => c.FacultyId == faculty.Id && c.OutAtUtc == null)
            .Select(c => new { c.Id, c.InAtUtc, c.InMethod, SubjectName = c.Subject != null ? c.Subject.Name : null })
            .FirstOrDefaultAsync(ct);
        var needsEndTime = await db.FacultyClocks.AsNoTracking()
            .Where(c => c.FacultyId == faculty.Id && c.OutMethod == ClockMethod.Auto && c.SessionId == null)
            .OrderBy(c => c.InAtUtc)
            .Select(c => new { c.Id, c.InAtUtc, SubjectName = c.Subject != null ? c.Subject.Name : null })
            .ToListAsync(ct);
        return Results.Ok(new
        {
            serverNowUtc = DateTime.UtcNow,
            running,
            needsEndTime,
            locationAvailable = tenant.Latitude != null,
            radiusMeters = tenant.GeofenceRadiusMeters,
            maxClassMinutes = tenant.MaxClassMinutes,
            canClockIn = faculty.Active && (faculty.PayType == FacultyPayType.Package || faculty.HourlyRate > 0)
        });
    }

    private sealed record Proof(ClockMethod Method, Guid? VerifiedBy, double? Latitude, double? Longitude, double? Accuracy, double? Distance);

    /// <summary>Checks the QR code or the location and returns what to record, or an error to show.</summary>
    private static async Task<(Proof? Proof, IResult? Error)> CheckProofAsync(
        AcademyDbContext db, Guid tenantId, ClockMethod method, string? code, double? latitude, double? longitude, double? accuracy, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, ct);
        switch (method)
        {
            case ClockMethod.Qr:
            {
                var (issuer, error) = CheckInCodes.Verify(tenant.CheckInSecret, tenantId, code, DateTimeOffset.UtcNow);
                if (error is not null) return (null, ApiResults.Bad(error));
                // The admin who showed the code must still be allowed to: a deactivated account's codes stop working.
                var role = await db.Users.Where(u => u.Id == issuer && u.Active).Select(u => u.Role).FirstOrDefaultAsync(ct);
                if (!RolePermissions.Has(role, Permissions.AttendanceManage))
                    return (null, ApiResults.Bad("That code was shown by an account that can no longer check faculty in."));
                return (new Proof(ClockMethod.Qr, issuer, null, null, null, null), null);
            }
            case ClockMethod.Location:
            {
                if (tenant.Latitude is not { } academyLat || tenant.Longitude is not { } academyLon)
                    return (null, ApiResults.Bad("The academy has not set its location, so you can only clock in by scanning the admin's QR code."));
                if (latitude is not { } lat || longitude is not { } lon || lat is < -90 or > 90 || lon is < -180 or > 180)
                    return (null, ApiResults.Bad("Your location could not be read. Allow location access for this site and try again."));
                var acc = accuracy is > 0 ? accuracy.Value : MaxAccuracyMeters;
                if (acc > MaxAccuracyMeters)
                    return (null, ApiResults.Bad("Your phone's location is not precise enough. Turn on GPS / precise location and try again."));

                var distance = Math.Round(Geo.DistanceMeters(academyLat, academyLon, lat, lon));
                if (distance > tenant.GeofenceRadiusMeters + Math.Min(acc, MaxAccuracyAllowance))
                    return (null, ApiResults.Bad(
                        $"You are about {FormatDistance(distance)} from the academy. Location clock-in works within {tenant.GeofenceRadiusMeters} m — move closer, or ask an admin to show the QR code."));
                return (new Proof(ClockMethod.Location, null, Math.Round(lat, 6), Math.Round(lon, 6), Math.Round(acc), distance), null);
            }
            default:
                return (null, ApiResults.Bad("Clock in by scanning the admin's QR code, or by location."));
        }
    }

    private static string FormatDistance(double meters) => meters >= 1000 ? $"{meters / 1000:0.0} km" : $"{meters:0} m";

    private static async Task<IResult> ClockIn(ClockInRequest request, AcademyDbContext db, ICurrentUser currentUser, HttpContext http, CancellationToken ct)
    {
        Infrastructure.Usage.UsageTracker.Tag(http, request.Method == ClockMethod.Qr ? "QR clock-in" : request.Method == ClockMethod.Location ? "Location clock-in" : "Faculty portal");
        if (await CurrentFacultyAsync(db, currentUser, ct) is not { } faculty) return NotLinked();
        if (!faculty.Active) return ApiResults.Bad("Your faculty record is marked as not teaching. Ask the academy to update it.");
        if (faculty.PayType == FacultyPayType.Hourly && faculty.HourlyRate <= 0) return ApiResults.Bad("The academy has not set your hourly rate yet.");
        if (request.SubjectId is { } subjectId
            && !await db.FacultySubjects.AnyAsync(l => l.FacultyId == faculty.Id && l.SubjectId == subjectId, ct))
            return ApiResults.Bad("Pick one of the subjects you teach.");

        var (proof, error) = await CheckProofAsync(db, currentUser.TenantId!.Value, request.Method, request.Code, request.Latitude, request.Longitude, request.Accuracy, ct);
        if (error is not null) return error;

        return await db.InTransactionAsync<IResult>(async () =>
        {
            if (!await db.LockFacultyAsync(faculty.Id, ct)) return NotLinked();
            if (await db.FacultyClocks.AnyAsync(c => c.FacultyId == faculty.Id && c.OutAtUtc == null, ct))
                return ApiResults.Conflict("You are already clocked in. Clock out of that class first.");

            var clock = new FacultyClock
            {
                FacultyId = faculty.Id,
                SubjectId = request.SubjectId,
                InAtUtc = DateTime.UtcNow,
                InMethod = proof!.Method,
                InVerifiedByUserId = proof.VerifiedBy,
                InLatitude = proof.Latitude,
                InLongitude = proof.Longitude,
                InAccuracyMeters = proof.Accuracy,
                InDistanceMeters = proof.Distance
            };
            db.FacultyClocks.Add(clock);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { clock.Id, clock.InAtUtc, clock.InMethod });
        }, ct);
    }

    private static async Task<IResult> ClockOut(ClockOutRequest request, AcademyDbContext db, ICurrentUser currentUser, HttpContext http, CancellationToken ct)
    {
        Infrastructure.Usage.UsageTracker.Tag(http, request.Method == ClockMethod.Qr ? "QR clock-in" : request.Method == ClockMethod.Location ? "Location clock-in" : "Faculty portal");
        if (await CurrentFacultyAsync(db, currentUser, ct) is not { } faculty) return NotLinked();
        var (proof, error) = await CheckProofAsync(db, currentUser.TenantId!.Value, request.Method, request.Code, request.Latitude, request.Longitude, request.Accuracy, ct);
        if (error is not null) return error;

        return await db.InTransactionAsync<IResult>(async () =>
        {
            if (!await db.LockFacultyAsync(faculty.Id, ct)) return NotLinked();
            var clock = await db.FacultyClocks.FirstOrDefaultAsync(c => c.FacultyId == faculty.Id && c.OutAtUtc == null, ct);
            if (clock is null) return ApiResults.Conflict("You are not clocked in.");

            var now = DateTime.UtcNow;
            string? note = null;
            if (!string.IsNullOrWhiteSpace(request.EndTime))
            {
                var (end, endError) = CorrectedEnd(clock.InAtUtc, request.EndTime, now);
                if (endError is not null) return endError;
                clock.CorrectedEndUtc = end;
                note = $"End time entered by the faculty member: class ended {Clock(end!.Value)}, clocked out at {DayAndClock(now)}";
            }

            clock.OutAtUtc = now;
            clock.OutMethod = proof!.Method;
            clock.OutVerifiedByUserId = proof.VerifiedBy;
            clock.OutLatitude = proof.Latitude;
            clock.OutLongitude = proof.Longitude;
            clock.OutAccuracyMeters = proof.Accuracy;
            clock.OutDistanceMeters = proof.Distance;
            return Results.Ok(await FinishAsync(db, clock, request.Topic, note, ct));
        }, ct);
    }

    private static async Task<IResult> FacultyEndTime(Guid id, EndTimeRequest request, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (await CurrentFacultyAsync(db, currentUser, ct) is not { } faculty) return NotLinked();
        return await CompleteAutoClosedAsync(db, id, faculty.Id, request, "End time entered later by the faculty member", ct);
    }

    private static async Task<IResult> CompleteAutoClosedAsync(
        AcademyDbContext db, Guid id, Guid? onlyFacultyId, EndTimeRequest request, string note, CancellationToken ct)
    {
        var facultyId = await db.FacultyClocks.Where(c => c.Id == id && (onlyFacultyId == null || c.FacultyId == onlyFacultyId))
            .Select(c => (Guid?)c.FacultyId).FirstOrDefaultAsync(ct);
        if (facultyId is null) return Results.NotFound();
        return await db.InTransactionAsync<IResult>(async () =>
        {
            if (!await db.LockFacultyAsync(facultyId.Value, ct)) return Results.NotFound();
            var clock = await db.FacultyClocks.FirstAsync(c => c.Id == id, ct);
            if (!clock.NeedsEndTime) return ApiResults.Conflict("This class already has its end time.");

            var (end, error) = CorrectedEnd(clock.InAtUtc, request.EndTime, clock.OutAtUtc!.Value);
            if (error is not null) return error;
            clock.CorrectedEndUtc = end;
            return Results.Ok(await FinishAsync(db, clock, request.Topic, $"{note}: class ended {Clock(end!.Value)} (it was left running and closed at midnight)", ct));
        }, ct);
    }

    /// <summary>
    /// "I forgot to clock in": the faculty member enters a past class by hand. It is clearly marked as a manual request
    /// and always waits for approval, so it never carries the trust of a QR check-in.
    /// </summary>
    private static async Task<IResult> Request(ManualRequest request, AcademyDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (await CurrentFacultyAsync(db, currentUser, ct) is not { } faculty) return NotLinked();
        if (!faculty.Active) return ApiResults.Bad("Your faculty record is marked as not teaching. Ask the academy to update it.");
        if (faculty.PayType == FacultyPayType.Hourly && faculty.HourlyRate <= 0) return ApiResults.Bad("The academy has not set your hourly rate yet.");
        if (!Validate.Reason(request.Reason)) return ApiResults.Bad("Say why the class was not clocked in, e.g. \"Phone battery was dead\".");
        if (request.Date is not { } date) return ApiResults.Bad("Choose the date of the class.");
        var today = AcademyClock.LocalDate(DateTime.UtcNow);
        if (date > today) return ApiResults.Bad("The date cannot be in the future.");
        if (date < today.AddDays(-ManualRequestMaxDaysBack)) return ApiResults.Bad($"Only classes from the last {ManualRequestMaxDaysBack} days can be entered. Ask the academy for older ones.");
        if (LocalTimeOn(date, request.StartTime) is not { } start || LocalTimeOn(date, request.EndTime) is not { } end)
            return ApiResults.Bad("Enter the time the class started and the time it ended.");
        if (end <= start) return ApiResults.Bad("The end time must be after the start time.");
        if (end > DateTime.UtcNow) return ApiResults.Bad("The class must already have ended.");
        if (request.SubjectId is { } subjectId
            && !await db.FacultySubjects.AnyAsync(l => l.FacultyId == faculty.Id && l.SubjectId == subjectId, ct))
            return ApiResults.Bad("Pick one of the subjects you teach.");

        return await db.InTransactionAsync<IResult>(async () =>
        {
            if (!await db.LockFacultyAsync(faculty.Id, ct)) return NotLinked();
            // The same time cannot be claimed twice: not over another class that day, nor over one running now.
            var startTime = TimeOnly.FromDateTime(AcademyClock.ToLocal(start));
            var endTime = TimeOnly.FromDateTime(AcademyClock.ToLocal(end));
            var clash = await db.FacultySessions.AsNoTracking()
                .Where(s => s.FacultyId == faculty.Id && s.TaughtOn == date && s.Status == LedgerStatus.Posted
                            && s.Review != SessionReview.Rejected && s.StartTime != null && s.EndTime != null
                            && s.StartTime < endTime && s.EndTime > startTime)
                .Select(s => new { s.StartTime, s.EndTime })
                .FirstOrDefaultAsync(ct);
            if (clash is not null)
                return ApiResults.Bad($"You already have a class from {clash.StartTime:HH\\:mm} to {clash.EndTime:HH\\:mm} that day. Classes cannot overlap.");
            if (await db.FacultyClocks.AnyAsync(c => c.FacultyId == faculty.Id && c.SessionId == null && c.InAtUtc < end && (c.OutAtUtc ?? DateTime.UtcNow) > start, ct))
                return ApiResults.Bad("That time overlaps a class you clocked into. Finish that class first.");

            var clock = new FacultyClock
            {
                FacultyId = faculty.Id,
                SubjectId = request.SubjectId,
                InAtUtc = start,
                InMethod = ClockMethod.Manual,
                OutAtUtc = end,
                OutMethod = ClockMethod.Manual,
                Reason = request.Reason!.Trim()
            };
            db.FacultyClocks.Add(clock);
            return Results.Ok(await FinishAsync(db, clock, request.Topic, $"Manual request — forgot to clock in: {clock.Reason}", ct));
        }, ct);
    }

    /// <summary>
    /// Ends a class and records its teaching time. Under a minute is treated as a mistaken tap and discarded.
    /// Approved straight away only when both ends were proved by an admin's QR code, nobody typed in a time,
    /// and the class is within the academy's maximum class length.
    /// </summary>
    private static async Task<object> FinishAsync(AcademyDbContext db, FacultyClock clock, string? topic, string? note, CancellationToken ct)
    {
        var end = clock.CorrectedEndUtc ?? clock.OutAtUtc ?? DateTime.UtcNow;
        clock.OutAtUtc ??= end;
        var minutes = (int)Math.Floor((end - clock.InAtUtc).TotalMinutes);
        if (minutes < 1)
        {
            db.FacultyClocks.Remove(clock);
            await db.SaveChangesAsync(ct);
            return new { cancelled = true, minutes = 0, amount = 0m, review = (SessionReview?)null };
        }

        var faculty = await db.Faculty.AsNoTracking().FirstAsync(f => f.Id == clock.FacultyId, ct);
        var settings = await db.Tenants.Where(t => t.Id == faculty.TenantId).Select(t => new { t.MaxClassMinutes, t.AutoApprovePackageClasses }).FirstAsync(ct);
        var maxClass = settings.MaxClassMinutes;
        var recorded = Math.Min(minutes, FacultySession.MaxMinutes);
        var tooLong = minutes > maxClass;
        var typedIn = clock.CorrectedEndUtc is not null || clock.InMethod == ClockMethod.Manual;
        static bool Proved(ClockMethod? m) => m is ClockMethod.Qr or ClockMethod.Location;
        var packageTrusted = faculty.PayType == FacultyPayType.Package && settings.AutoApprovePackageClasses
                             && Proved(clock.InMethod) && Proved(clock.OutMethod);
        var trusted = !typedIn && !tooLong && (clock.InMethod == ClockMethod.Qr && clock.OutMethod == ClockMethod.Qr || packageTrusted);
        // Package faculty are not paid by the hour; their classes are still recorded, as attendance.
        var rate = faculty.PayType == FacultyPayType.Hourly ? faculty.HourlyRate : 0;

        var reasons = new List<string>();
        if (note is not null) reasons.Add(note);
        if (tooLong) reasons.Add($"Longer than the academy's {maxClass / 60.0:0.#} h maximum class length ({minutes / 60} h {minutes % 60} min)");
        if (clock.InMethod == ClockMethod.Location || clock.OutMethod == ClockMethod.Location) reasons.Add("Clocked by location");

        var start = AcademyClock.ToLocal(clock.InAtUtc);
        var session = new FacultySession
        {
            FacultyId = clock.FacultyId,
            TaughtOn = DateOnly.FromDateTime(start),
            StartTime = TimeOnly.FromDateTime(start),
            EndTime = TimeOnly.FromDateTime(AcademyClock.ToLocal(end)),
            Minutes = recorded,
            HourlyRate = rate,
            Amount = FacultySession.AmountFor(recorded, rate),
            SubjectId = clock.SubjectId,
            Topic = Validate.Text(topic, 200),
            Review = trusted ? SessionReview.Approved : SessionReview.Pending,
            ReviewNote = !trusted ? string.Join(". ", reasons.Distinct())
                : clock.InMethod == ClockMethod.Qr && clock.OutMethod == ClockMethod.Qr ? "Verified by QR at clock-in and clock-out"
                : "Package faculty attendance, approved automatically",
            ReviewedByUserId = trusted ? clock.OutVerifiedByUserId : null,
            ReviewedAtUtc = trusted ? DateTime.UtcNow : null
        };
        db.FacultySessions.Add(session);
        clock.SessionId = session.Id;
        await db.SaveChangesAsync(ct);
        return new { cancelled = false, minutes = recorded, amount = session.Amount, review = (SessionReview?)session.Review };
    }
}

/// <summary>
/// Looks after forgotten clock-outs, every few minutes: emails a faculty member once when a class has run past the
/// academy's maximum class length, and at midnight closes any class still running, so it no longer blocks the next
/// clock-in. A closed class pays nothing until someone enters when it really ended.
/// </summary>
public sealed class ClassWatchService(IServiceScopeFactory scopes, ILogger<ClassWatchService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AcademyDbContext>();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>();
                await RunAsync(db, options.Value, DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Checking running classes failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public sealed record Result(int Reminded, int Closed);

    public static async Task<Result> RunAsync(AcademyDbContext db, AppOptions options, DateTime nowUtc, CancellationToken ct)
    {
        var today = AcademyClock.LocalDate(nowUtc);
        var running = await db.FacultyClocks.IgnoreQueryFilters()
            .Where(c => c.OutAtUtc == null)
            .Select(c => new
            {
                Clock = c,
                FacultyName = c.Faculty.FullName,
                Email = db.Users.IgnoreQueryFilters().Where(u => u.FacultyId == c.FacultyId && u.Active).Select(u => u.Email).FirstOrDefault() ?? c.Faculty.Email,
                MaxClassMinutes = db.Tenants.Where(t => t.Id == c.TenantId).Select(t => t.MaxClassMinutes).First()
            })
            .ToListAsync(ct);

        var emails = new EmailQueue(db);
        var portal = string.IsNullOrWhiteSpace(options.PublicBaseUrl) ? "the faculty portal" : $"{options.PublicBaseUrl.TrimEnd('/')}/teacher";
        int reminded = 0, closed = 0;
        foreach (var row in running)
        {
            var clock = row.Clock;
            var started = AcademyClock.ToLocal(clock.InAtUtc).ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant();
            if (AcademyClock.LocalDate(clock.InAtUtc) < today)
            {
                // Midnight has passed: close it, with no pay until the real end time is entered.
                clock.OutAtUtc = AcademyClock.UtcStartOf(AcademyClock.LocalDate(clock.InAtUtc).AddDays(1));
                clock.OutMethod = ClockMethod.Auto;
                closed++;
                if (!string.IsNullOrWhiteSpace(row.Email))
                    emails.Enqueue(clock.TenantId, row.Email, "Your class was not clocked out",
                        $"Hello {row.FacultyName},\n\nYour class that started at {started} on {AcademyClock.LocalDate(clock.InAtUtc):dd MMM} was never clocked out, so it was closed at midnight.\n\n"
                        + $"It will not be paid until you enter the time it really ended. Open {portal} and tap \"Enter end time\".\n", "ClassReminder");
            }
            else if (clock.ReminderSentAtUtc is null && (nowUtc - clock.InAtUtc).TotalMinutes > row.MaxClassMinutes)
            {
                clock.ReminderSentAtUtc = nowUtc;
                reminded++;
                if (!string.IsNullOrWhiteSpace(row.Email))
                    emails.Enqueue(clock.TenantId, row.Email, "Did you forget to clock out?",
                        $"Hello {row.FacultyName},\n\nYou clocked into a class at {started} and are still clocked in, longer than the academy's usual class length.\n\n"
                        + $"If the class is over, open {portal} and clock out. If it ended a while ago, you can enter the real end time when you clock out.\n", "ClassReminder");
            }
        }
        if (reminded + closed > 0) await db.SaveChangesAsync(ct);
        return new Result(reminded, closed);
    }
}
