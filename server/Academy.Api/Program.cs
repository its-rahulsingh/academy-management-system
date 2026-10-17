using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);

// Render forwards requests to the port in its PORT environment variable.
// Locally, you can override the port with `dotnet run --urls http://localhost:5180`.
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var port)
    && port is > 0 and <= 65535)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var app = builder.Build();

// When deployed, Angular's compiled files are copied into wwwroot by Docker.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    service = "academy-api",
    utcTime = DateTimeOffset.UtcNow
}));

app.MapGet("/api/welcome", () => Results.Ok(new
{
    application = "Academy ERP",
    message = "Angular and ASP.NET Core 10 are connected successfully!",
    phase = "Starter",
    modulesPlanned = new[] { "Students", "Batches", "Enrollments" }
}));

// Angular client-side routes (/dashboard, etc.) load the SPA.
// Unknown API routes should NOT return Angular's index.html.
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
