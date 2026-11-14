using System.Security.Cryptography;
using System.Text;

namespace Academy.Api.Infrastructure.Web;

/// <summary>
/// Double-submit CSRF protection for cookie sessions. The browser app echoes the XSRF-TOKEN cookie
/// in the X-XSRF-TOKEN header on every state-changing request (Angular's HttpClient does this by default).
/// </summary>
public sealed class CsrfMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    private const string CookieName = "XSRF-TOKEN";
    private const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>Server-to-server callbacks carry no cookies and are authenticated by their own signature.</summary>
    private static readonly PathString[] Exempt = ["/api/billing/webhooks"];

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!request.Path.StartsWithSegments("/api") || Exempt.Any(p => request.Path.StartsWithSegments(p)))
        {
            await next(context);
            return;
        }

        if (!request.Cookies.TryGetValue(CookieName, out var token) || string.IsNullOrEmpty(token))
        {
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            context.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = false,
                Secure = !environment.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Path = "/"
            });
        }

        var safe = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method);
        if (!safe)
        {
            var origin = request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin)
                && (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
                    || !string.Equals(originUri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var supplied = request.Headers[HeaderName].ToString();
            if (string.IsNullOrEmpty(supplied)
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(token)))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "Security token missing or expired. Refresh and retry." });
                return;
            }
        }

        await next(context);
    }
}
