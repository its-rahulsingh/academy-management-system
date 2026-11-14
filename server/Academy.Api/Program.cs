using Academy.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Render tells the container which port to listen on.
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var port) && port is > 0 and <= 65535)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.AddAcademyServices();

var app = builder.Build();
await app.InitializeDatabaseAsync();
app.UseAcademyPipeline();
app.MapAcademyEndpoints();
app.Run();

/// <summary>Exposed so the integration tests can host the real application.</summary>
public partial class Program;
