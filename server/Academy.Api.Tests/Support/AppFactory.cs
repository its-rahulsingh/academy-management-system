using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Academy.Api.Data;
using Academy.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.Api.Tests.Support;

/// <summary>
/// Hosts the real application against a throwaway PostgreSQL database, created for the test run and
/// dropped afterwards. Point ACADEMY_TEST_POSTGRES at any server you can create databases on.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string OwnerEmail = "owner@platform.test";
    public const string Password = "correct-horse-battery";

    private static readonly string PostgresServer =
        Environment.GetEnvironmentVariable("ACADEMY_TEST_POSTGRES") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    private readonly string _database = $"academy_test_{Guid.NewGuid():N}";

    private string ConnectionString => new NpgsqlConnectionStringBuilder(PostgresServer) { Database = _database, Pooling = false }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:AcademyDb", ConnectionString);
        builder.UseSetting("OWNER_EMAIL", OwnerEmail);
        builder.UseSetting("OWNER_PASSWORD", Password);
        builder.UseSetting("BackgroundJobs:Enabled", "false");
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "100000");
        builder.UseSetting("RateLimiting:PublicPermitsPerMinute", "100000");
    }

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(PostgresServer) { Database = "postgres" }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"create database {_database}", connection);
        await command.ExecuteNonQueryAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(PostgresServer) { Database = "postgres" }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"drop database if exists {_database} with (force)", connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A browser-like client: keeps cookies and echoes the CSRF token.</summary>
    public HttpClient NewBrowser() => CreateDefaultClient(new BrowserHandler());

    /// <summary>Direct database access that bypasses tenant filters, for arranging and asserting state.</summary>
    public async Task<T> WithDbAsync<T>(Func<AcademyDbContext, Task<T>> work)
    {
        var options = new DbContextOptionsBuilder<AcademyDbContext>().UseNpgsql(ConnectionString).Options;
        await using var db = new AcademyDbContext(options, new SystemCurrentUser());
        return await work(db);
    }

    public async Task<HttpClient> OwnerAsync()
    {
        var client = NewBrowser();
        await client.LoginAsync(null, OwnerEmail);
        return client;
    }

    public async Task<TestAcademy> NewAcademyAsync(int trialDays = 14)
    {
        var owner = await OwnerAsync();
        var plans = await owner.GetJsonAsync("/api/platform/plans");
        var code = $"t{Guid.NewGuid():N}"[..16];
        var adminEmail = $"admin@{code}.test";

        var response = await owner.PostAsJsonAsync("/api/platform/tenants", new
        {
            name = $"Academy {code}",
            code,
            planId = plans.AsArray().Single(p => p!["code"]!.GetValue<string>() == "starter")!["id"]!.GetValue<Guid>(),
            trialDays,
            superAdmin = new { name = "Super Admin", email = adminEmail, password = Password }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.ReadJsonAsync())["id"]!.GetValue<Guid>();

        var admin = NewBrowser();
        await admin.LoginAsync(code, adminEmail);
        return new TestAcademy(this, id, code, admin, owner);
    }
}

public sealed record TestAcademy(AppFactory Factory, Guid Id, string Code, HttpClient Admin, HttpClient Owner)
{
    public async Task<HttpClient> NewUserAsync(string role)
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@{Code}.test";
        var created = await Admin.PostAsJsonAsync("/api/users", new { displayName = $"{role} User", email, role, password = AppFactory.Password });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var client = Factory.NewBrowser();
        await client.LoginAsync(Code, email);
        return client;
    }

    public async Task<Guid> NewCourseAsync(string name = "Foundation", decimal fee = 10000)
    {
        var response = await Admin.PostAsJsonAsync("/api/courses", new { name, fee });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync())["id"]!.GetValue<Guid>();
    }

    public async Task<Guid> NewStudentAsync(Guid courseId, decimal fee = 10000, string name = "Asha Rao", string email = "")
    {
        var response = await Admin.PostAsJsonAsync("/api/students", new { fullName = name, email, courseId, feeAmount = fee });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync())["id"]!.GetValue<Guid>();
    }

    public Task<HttpResponseMessage> PayAsync(Guid studentId, decimal amount, HttpClient? client = null) =>
        (client ?? Admin).PostAsJsonAsync("/api/payments", new { studentId, amount, method = "Cash" });

    public async Task<JsonNode> StudentAsync(Guid studentId) =>
        (await Admin.GetJsonAsync($"/api/students/{studentId}"))["student"]!;
}

/// <summary>Does what a browser does for this app: stores cookies and sends the XSRF cookie back as a header.</summary>
public sealed class BrowserHandler : DelegatingHandler
{
    private readonly CookieContainer _cookies = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        if (_cookies.GetCookies(uri)["XSRF-TOKEN"] is null && request.Method != HttpMethod.Get)
            await SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(uri, "/api/health")), cancellationToken);

        var header = _cookies.GetCookieHeader(uri);
        if (header.Length > 0) request.Headers.Add("Cookie", header);
        if (_cookies.GetCookies(uri)["XSRF-TOKEN"] is { } token && !request.Headers.Contains("X-XSRF-TOKEN"))
            request.Headers.Add("X-XSRF-TOKEN", token.Value);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            foreach (var cookie in setCookies) _cookies.SetCookies(uri, cookie);
        return response;
    }
}

public static class HttpExtensions
{
    public static async Task<JsonNode> LoginAsync(this HttpClient client, string? academyCode, string email, string password = AppFactory.Password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { academyCode, email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    public static async Task<JsonNode> GetJsonAsync(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"GET {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.ReadJsonAsync();
    }

    public static async Task<JsonNode> ReadJsonAsync(this HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    public static async Task<string> ErrorAsync(this HttpResponseMessage response) =>
        (await response.ReadJsonAsync())["error"]!.GetValue<string>();

    public static decimal Money(this JsonNode node, string property) => node[property]!.GetValue<decimal>();
}

[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<AppFactory>
{
    public const string Name = "app";
}
