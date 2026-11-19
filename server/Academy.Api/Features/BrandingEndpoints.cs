using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// An academy's logo and tagline. The Super Admin sets them from the academy, the platform owner from the console;
/// both go through <see cref="BrandingStore"/>. The logo is served publicly so the sign-in and admission pages can show it.
/// </summary>
public static class BrandingEndpoints
{
    public const int MaxTaglineLength = 120;

    public sealed record BrandingRequest(string? Tagline);
    public sealed record LogoRequest(string? DataUrl);

    public static string? LogoUrl(Guid tenantId, int? version) =>
        version is { } v ? $"/api/public/academies/{tenantId}/logo?v={v}" : null;

    /// <summary>Academy side: the Super Admin's own branding.</summary>
    public static void MapBrandingEndpoints(this RouteGroupBuilder academy)
    {
        var group = academy.MapGroup("/branding").RequirePermission(Permissions.BrandingManage);
        group.MapGet("", (AcademyDbContext db, ICurrentUser user, CancellationToken ct) => Get(db, user.TenantId!.Value, ct));
        group.MapPut("", (BrandingRequest request, AcademyDbContext db, TenantAccessService access, ICurrentUser user, CancellationToken ct) =>
            BrandingStore.SetTaglineAsync(db, access, user.TenantId!.Value, request.Tagline, ct));
        group.MapPut("/logo", (LogoRequest request, AcademyDbContext db, TenantAccessService access, ICurrentUser user, CancellationToken ct) =>
            BrandingStore.SetLogoAsync(db, access, user.TenantId!.Value, request.DataUrl, ct));
        group.MapDelete("/logo", (AcademyDbContext db, TenantAccessService access, ICurrentUser user, CancellationToken ct) =>
            BrandingStore.RemoveLogoAsync(db, access, user.TenantId!.Value, ct));
    }

    /// <summary>Platform side: the owner sets branding for any academy.</summary>
    public static void MapPlatformBrandingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/tenants/{id:guid}/branding").RequireAuthorization(Policies.Owner);
        group.MapGet("", (Guid id, AcademyDbContext db, CancellationToken ct) => Get(db, id, ct));
        group.MapPut("", (Guid id, BrandingRequest request, AcademyDbContext db, TenantAccessService access, CancellationToken ct) =>
            BrandingStore.SetTaglineAsync(db, access, id, request.Tagline, ct));
        group.MapPut("/logo", (Guid id, LogoRequest request, AcademyDbContext db, TenantAccessService access, CancellationToken ct) =>
            BrandingStore.SetLogoAsync(db, access, id, request.DataUrl, ct));
        group.MapDelete("/logo", (Guid id, AcademyDbContext db, TenantAccessService access, CancellationToken ct) =>
            BrandingStore.RemoveLogoAsync(db, access, id, ct));
    }

    /// <summary>Anonymous: what the sign-in page shows once an academy code is typed, and the logo image itself.</summary>
    public static void MapPublicBrandingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/academies");
        group.MapGet("/{code}", PublicProfile).RequireRateLimiting(PublicRateLimitPolicy);
        group.MapGet("/{tenantId:guid}/logo", Logo);
    }

    public const string PublicRateLimitPolicy = "public";

    private static async Task<IResult> Get(AcademyDbContext db, Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Id, t.Code, t.Name, t.Tagline, t.LogoVersion })
            .FirstOrDefaultAsync(ct);
        return tenant is null
            ? Results.NotFound()
            : Results.Ok(new { tenant.Id, tenant.Code, tenant.Name, tenant.Tagline, logoUrl = LogoUrl(tenant.Id, tenant.LogoVersion) });
    }

    private static async Task<IResult> PublicProfile(string code, AcademyDbContext db, CancellationToken ct)
    {
        var normalized = code.Trim().ToLowerInvariant();
        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Code == normalized && t.Status == TenantStatus.Active)
            .Select(t => new { t.Id, t.Name, t.Tagline, t.LogoVersion })
            .FirstOrDefaultAsync(ct);
        return tenant is null
            ? Results.NotFound()
            : Results.Ok(new { tenant.Name, tenant.Tagline, logoUrl = LogoUrl(tenant.Id, tenant.LogoVersion) });
    }

    private static async Task<IResult> Logo(Guid tenantId, int? v, AcademyDbContext db, HttpContext http, CancellationToken ct)
    {
        var logo = await db.TenantLogos.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.TenantId == tenantId)
            .Select(l => new { l.ContentType, l.Data })
            .FirstOrDefaultAsync(ct);
        if (logo is null) return Results.NotFound();

        // The URL changes with every new logo, so a versioned request can be cached for good.
        http.Response.Headers.CacheControl = v is null ? "public, max-age=300" : "public, max-age=31536000, immutable";
        http.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
        return Results.File(logo.Data, logo.ContentType);
    }
}

/// <summary>Validation and storage shared by the academy and platform branding endpoints.</summary>
public static class BrandingStore
{
    public static async Task<IResult> SetTaglineAsync(
        AcademyDbContext db, TenantAccessService access, Guid tenantId, string? tagline, CancellationToken ct)
    {
        var text = (tagline ?? "").Trim();
        if (text.Length > BrandingEndpoints.MaxTaglineLength)
            return ApiResults.Bad($"Keep the tagline to {BrandingEndpoints.MaxTaglineLength} characters.");

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return Results.NotFound();
        tenant.Tagline = text;
        await db.SaveChangesAsync(ct);
        access.Invalidate(tenantId);
        return Results.Ok(new { tenant.Tagline });
    }

    public static async Task<IResult> SetLogoAsync(
        AcademyDbContext db, TenantAccessService access, Guid tenantId, string? dataUrl, CancellationToken ct)
    {
        if (!TryDecode(dataUrl, out var bytes, out var contentType, out var error)) return ApiResults.Bad(error);

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return Results.NotFound();

        var logo = await db.TenantLogos.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.TenantId == tenantId, ct);
        if (logo is null)
        {
            logo = new TenantLogo { TenantId = tenantId };
            db.TenantLogos.Add(logo);
        }
        logo.ContentType = contentType;
        logo.Data = bytes;
        logo.UpdatedAtUtc = DateTime.UtcNow;
        tenant.LogoVersion = (tenant.LogoVersion ?? 0) + 1;

        await db.SaveChangesAsync(ct);
        access.Invalidate(tenantId);
        return Results.Ok(new { logoUrl = BrandingEndpoints.LogoUrl(tenantId, tenant.LogoVersion) });
    }

    public static async Task<IResult> RemoveLogoAsync(AcademyDbContext db, TenantAccessService access, Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return Results.NotFound();

        // One SaveChanges, so the image and the version that points at it go together.
        if (await db.TenantLogos.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.TenantId == tenantId, ct) is { } logo)
            db.TenantLogos.Remove(logo);
        tenant.LogoVersion = null;
        await db.SaveChangesAsync(ct);
        access.Invalidate(tenantId);
        return Results.Ok(new { success = true });
    }

    /// <summary>
    /// Accepts a base64 data URL of a PNG, JPEG or WebP image. The type is read from the file's own bytes,
    /// never from what the browser claims, so nothing but those three formats is ever served back.
    /// SVG is refused: it can carry script.
    /// </summary>
    public static bool TryDecode(string? dataUrl, out byte[] bytes, out string contentType, out string error)
    {
        bytes = [];
        contentType = "";
        error = "Choose a PNG, JPEG or WebP image.";

        var comma = (dataUrl ?? "").IndexOf(',');
        if (comma < 0 || !dataUrl![..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return false;

        var payload = dataUrl.AsSpan(comma + 1);
        if (payload.Length > TenantLogo.MaxBytes * 4 / 3 + 4)
        {
            error = $"The image is too large. Use one under {TenantLogo.MaxBytes / 1024} KB.";
            return false;
        }

        var buffer = new byte[payload.Length * 3 / 4 + 3];
        if (!Convert.TryFromBase64Chars(payload, buffer, out var length)) return false;
        bytes = buffer[..length];
        if (bytes.Length is 0 or > TenantLogo.MaxBytes)
        {
            error = $"The image is too large. Use one under {TenantLogo.MaxBytes / 1024} KB.";
            return false;
        }

        contentType = Sniff(bytes) ?? "";
        return contentType.Length > 0;
    }

    private static string? Sniff(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "image/png";
        if (data.StartsWith((byte[])[0xFF, 0xD8, 0xFF])) return "image/jpeg";
        if (data.Length > 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }
}
