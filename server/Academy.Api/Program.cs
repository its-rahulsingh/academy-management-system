using Academy.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Render forwards traffic to the port in its PORT environment variable.
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var port)
    && port is > 0 and <= 65535)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// The secret is supplied by Render: Environment -> ConnectionStrings__AcademyDb.
// Locally, use .NET user-secrets or a process environment variable.
var connectionString = builder.Configuration.GetConnectionString("AcademyDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Missing ConnectionStrings:AcademyDb. Configure it in local user-secrets " +
        "or as ConnectionStrings__AcademyDb in Render Environment.");
}

builder.Services.AddDbContext<AcademyDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

// Availability endpoint is intentionally independent of database sleep/restarts.
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    service = "academy-api",
    utcTime = DateTimeOffset.UtcNow
}));

// Read-only check. Tests database connectivity *and* the EF-created table.
// Do not expose database credentials or raw errors to browsers.
app.MapGet("/api/db/status", async (AcademyDbContext db, CancellationToken ct) =>
{
    try
    {
        var batchCount = await db.Batches.CountAsync(ct);
        return Results.Ok(new { status = "connected", schemaReady = true, batchCount });
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Academy database check failed");
        return Results.Json(new { status = "unavailable", schemaReady = false }, statusCode: 503);
    }
});

app.MapGet("/api/welcome", () => Results.Ok(new
{
    application = "Academy ERP",
    message = "Angular, .NET 10 and Neon PostgreSQL are configured.",
    phase = "Database setup",
    modulesPlanned = new[] { "Students", "Batches", "Enrollments" }
}));

app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = "API endpoint not found" });
        return;
    }
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var indexPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
    if (!File.Exists(indexPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("Angular frontend is not built. Use npm start from client/ for development.");
        return;
    }
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
});

app.Run();
