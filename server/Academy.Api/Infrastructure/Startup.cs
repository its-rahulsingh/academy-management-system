using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Features;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Billing;
using Academy.Api.Infrastructure.Email;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Infrastructure;

public static class Startup
{
    public static void AddAcademyServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var connectionString = configuration.GetConnectionString("AcademyDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Missing ConnectionStrings:AcademyDb. Set the ConnectionStrings__AcademyDb environment variable.");

        services.AddDbContext<AcademyDbContext>(options => options.UseNpgsql(connectionString,
            npgsql => npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));

        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<TenantAccessService>();
        services.AddScoped<AcademicYearScope>();
        services.AddScoped<EmailQueue>();
        services.AddScoped<BillingService>();
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<Usage.UsageTracker>();

        services.Configure<SubscriptionOptions>(configuration.GetSection(SubscriptionOptions.Section));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.Section));
        services.Configure<RazorpayOptions>(configuration.GetSection(RazorpayOptions.Section));

        var email = configuration.GetSection(EmailOptions.Section).Get<EmailOptions>();
        if (email is { IsConfigured: true, UsesResend: true })
            services.AddHttpClient<IEmailTransport, ResendEmailTransport>(client => client.Timeout = TimeSpan.FromSeconds(20));
        else if (email is { IsConfigured: true, UsesBrevo: true })
            services.AddHttpClient<IEmailTransport, BrevoEmailTransport>(client => client.Timeout = TimeSpan.FromSeconds(20));
        else if (email is { IsConfigured: true })
            services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
        else
            services.AddSingleton<IEmailTransport, LogEmailTransport>();

        services.AddHttpClient<IPaymentGateway, RazorpayGateway>(client => client.Timeout = TimeSpan.FromSeconds(20));

        // Tests switch these off so nothing runs behind their back.
        if (configuration.GetValue("BackgroundJobs:Enabled", true))
        {
            services.AddHostedService<EmailDispatchService>();
            services.AddHostedService<BillingCycleService>();
            services.AddHostedService<ClassWatchService>();
            services.AddHostedService<Usage.UsageFlushService>();
        }

        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "AcademyFlow.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            // An API answers with status codes, never with redirects to a login page.
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            options.Events.OnValidatePrincipal = context =>
                context.HttpContext.RequestServices.GetRequiredService<SessionService>().ValidateAsync(context);
        });
        services.AddAcademyAuthorization();

        // Behind Render's proxy every request arrives from the proxy's address. Without this, the login
        // rate limit would be one shared bucket for all users. The proxy appends the real client address
        // last, so exactly one hop is trusted and a client-supplied X-Forwarded-For cannot spoof it.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        var authPermits = configuration.GetValue("RateLimiting:AuthPermitsPerMinute", 10);
        // Pages anyone can open without signing in (admission form, academy logo lookup).
        var publicPermits = configuration.GetValue("RateLimiting:PublicPermitsPerMinute", 30);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthEndpoints.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy(BrandingEndpoints.PublicRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = publicPermits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    public static void UseAcademyPipeline(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseForwardedHeaders();
            app.UseHsts();
        }

        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Something went wrong on the server. Please try again." });
        }));

        app.Use(async (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            // Nothing may be framed by another site. The marketing videos in /media are framed by our own landing page,
            // so they allow the same origin; every other page (the app itself) cannot be framed at all.
            context.Response.Headers.XFrameOptions = context.Request.Path.StartsWithSegments("/media") ? "SAMEORIGIN" : "DENY";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            await next();
        });

        app.UseDefaultFiles();
        app.UseStaticFiles();
        // The marketing videos live in the repository's marketing/web folder and are served at /media
        // (the landing page frames them). In the container they are copied next to the app.
        if (MarketingFolder(app.Environment.ContentRootPath) is { } marketing)
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(marketing),
                RequestPath = "/media"
            });
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        // After sign-in is known (role, academy) and around everything else, so refusals and errors are counted too.
        app.UseMiddleware<Usage.UsageMiddleware>();
        app.UseAuthorization();
        app.UseMiddleware<CsrfMiddleware>();
    }

    /// <summary>marketing/web: copied beside the app in the container, or found at the repository root when running from source.</summary>
    private static string? MarketingFolder(string contentRoot) =>
        new[] { Path.Combine(contentRoot, "marketing"), Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "marketing", "web")) }
            .FirstOrDefault(Directory.Exists);

    public static void MapAcademyEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", service = "academy-api", utcTime = DateTimeOffset.UtcNow }));
        app.MapGet("/api/welcome", () => Results.Ok(new { application = "Academy ERP", message = "Ojas API is running.", phase = "Ojas" }));

        app.MapAuthEndpoints();
        app.MapPlatformEndpoints();
        app.MapPlatformAnalyticsEndpoints();
        app.MapPlatformBrandingEndpoints();
        app.MapEmailAdminEndpoints();
        app.MapBillingEndpoints();
        app.MapPublicBrandingEndpoints();
        app.MapPublicPlanEndpoints();
        app.MapPublicAdmissionEndpoints();

        // Everything an academy does with its own data sits behind one gate: signed in, belongs to an
        // academy, and that academy's subscription is in good standing.
        var academy = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<TenantAccessFilter>();
        academy.MapAcademicYearEndpoints();
        academy.MapDashboardEndpoints();
        academy.MapCourseEndpoints();
        academy.MapSubjectEndpoints();
        academy.MapStudentEndpoints();
        academy.MapAdmissionEndpoints();
        academy.MapBrandingEndpoints();
        academy.MapPaymentEndpoints();
        academy.MapFacultyEndpoints();
        academy.MapFacultyPortalEndpoints();
        academy.MapAttendanceEndpoints();
        academy.MapStudentPortalEndpoints();
        academy.MapPromotionEndpoints();
        academy.MapFacultyAttendanceEndpoints();
        academy.MapExpenseEndpoints();
        academy.MapTestEndpoints();
        academy.MapReportEndpoints();
        academy.MapUserEndpoints();
        academy.MapAuditEndpoints();
        academy.MapAuditDashboardEndpoints();

        // Single-page app: any other GET returns index.html so the browser router can handle the path.
        app.MapFallback(async context =>
        {
            var isPage = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
            if (context.Request.Path.StartsWithSegments("/api") || !isPage)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { error = "API endpoint not found." });
                return;
            }

            var index = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
            if (!File.Exists(index))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Angular frontend is not built.");
                return;
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(index);
        });
    }

    /// <summary>Applies pending migrations (which include the price list), then makes sure a platform owner exists.</summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademyDbContext>();
        // The FreemiumPlans migration creates the Free, Basic and Pro price list, so a new database starts with it.
        await db.Database.MigrateAsync();

        // OWNER_* is the current name; ADMIN_* is still read so existing deployments keep working.
        var email = (app.Configuration["OWNER_EMAIL"] ?? app.Configuration["ADMIN_EMAIL"] ?? "").Trim().ToLowerInvariant();
        var password = app.Configuration["OWNER_PASSWORD"] ?? app.Configuration["ADMIN_PASSWORD"] ?? "";
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Role == Roles.Owner)) return;

        if (!Validate.Email(email) || !PasswordRules.IsValid(password))
        {
            app.Logger.LogWarning("No platform owner exists. Set OWNER_EMAIL and OWNER_PASSWORD (5+ characters) and restart to create one.");
            return;
        }

        var owner = new AppUser { Email = email, DisplayName = "Platform Owner", Role = Roles.Owner };
        scope.ServiceProvider.GetRequiredService<SessionService>().SetPassword(owner, password);
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        app.Logger.LogInformation("Platform owner account created for {Email}", email);
    }
}
