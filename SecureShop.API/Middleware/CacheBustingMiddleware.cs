namespace SecureShop.API.Middleware;

/// <summary>
/// ASP.NET Core middleware that appends cache-busting HTTP headers
/// (<c>Cache-Control: no-store</c>, <c>Pragma: no-cache</c>, <c>Expires: 0</c>)
/// to responses for paths that must never be served from a stale browser cache.
/// <para>
/// Targeted paths include the root, Swagger UI, OpenAPI JSON payload,
/// and all account pages (login, register, checkout) so users always
/// receive fresh auth state and product data.
/// </para>
/// </summary>
public class CacheBustingMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Initialises the middleware with the next pipeline delegate.</summary>
    public CacheBustingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Checks whether the request path should be cache-busted and, if so,
    /// hooks into <c>Response.OnStarting</c> to inject the headers before
    /// any bytes are written to the response stream.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Ensure docs UI, OpenAPI payload, and account pages are never served from stale browser cache.
        if (path == "/" ||
            path == "/docs" ||
            path.StartsWith("/docs/") ||
            path.StartsWith("/swagger") ||
            path.StartsWith("/api-docs") ||
            path.StartsWith("/swagger-ui") ||
            path.StartsWith("/account/") ||
            path.EndsWith("swagger-ui-bundle.js") ||
            path.EndsWith("swagger-ui-standalone-preset.js") ||
            path.EndsWith("swagger-ui.css") ||
            path.EndsWith("index.html") ||
            path.Contains(".json"))
        {
            // Set cache-busting headers before response is sent
            context.Response.OnStarting(() =>
            {
                if (context.Response.StatusCode == 200 || context.Response.StatusCode == 302)
                {
                    context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                    context.Response.Headers["Pragma"] = "no-cache";
                    context.Response.Headers["Expires"] = "0";
                }
                return Task.CompletedTask;
            });
        }

        await _next(context);
    }
}
