namespace SecureShop.API.Middleware;

/// <summary>
/// ASP.NET Core middleware that injects HTTP security headers on every non-Swagger response.
/// <para>
/// Headers always applied:
/// <list type="bullet">
///   <item><c>X-Content-Type-Options: nosniff</c> — prevents MIME sniffing.</item>
///   <item><c>X-XSS-Protection: 1; mode=block</c> — legacy XSS filter in older browsers.</item>
///   <item><c>Referrer-Policy: strict-origin-when-cross-origin</c></item>
///   <item><c>Permissions-Policy</c> — disables camera, microphone, and geolocation APIs.</item>
///   <item><c>Content-Security-Policy</c> — environment-specific CSP (see below).</item>
/// </list>
/// </para>
/// <para>
/// Development uses a permissive CSP that allows localhost origins and <c>SAMEORIGIN</c> framing.
/// Production uses a strict CSP with <c>frame-ancestors 'none'</c> and <c>X-Frame-Options: DENY</c>.
/// </para>
/// Swagger/API-docs paths are exempted to avoid breaking the interactive documentation UI.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _environment;

    /// <summary>Initialises the middleware with the next delegate and the hosting environment.</summary>
    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

    /// <summary>
    /// Injects security headers and then calls the next middleware.
    /// Swagger paths bypass header injection entirely.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Skip security headers only for Swagger endpoints (API documentation)
        var path = context.Request.Path.Value?.ToLower() ?? "";
        if (path.StartsWith("/swagger") || path.StartsWith("/api-docs"))
        {
            await _next(context);
            return;
        }

        var h = context.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-XSS-Protection"] = "1; mode=block";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        if (_environment.IsDevelopment())
        {
            // Development: allow localhost origins and same-origin framing for easier debugging.
            h["X-Frame-Options"] = "SAMEORIGIN";
            h["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://accounts.google.com https://code.jquery.com; " +
                "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://accounts.google.com; " +
                "img-src 'self' data: https:; " +
                "font-src 'self' data: https://cdnjs.cloudflare.com; " +
                "connect-src 'self' https://localhost:5001 http://localhost:5000 http://localhost:8080 https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://accounts.google.com https://api.cloudinary.com; " +
                "frame-src 'self' https://accounts.google.com https://*.google.com; " +
                "frame-ancestors 'self';";
        }
        else
        {
            // Production: strict framing controls and tighter CSP; no inline localhost origins.
            // Allow CDN resources for Bootstrap, Font Awesome, jQuery, and Google OAuth.
            h["X-Frame-Options"] = "DENY";
            h["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://accounts.google.com https://code.jquery.com; " +
                "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://accounts.google.com; " +
                "img-src 'self' data: https:; " +
                "font-src 'self' data: https://cdnjs.cloudflare.com; " +
                "connect-src 'self' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://accounts.google.com https://api.cloudinary.com; " +
                "frame-src 'self' https://accounts.google.com https://*.google.com; " +
                "base-uri 'self'; " +
                "form-action 'self'; " +
                "frame-ancestors 'none';";
        }

        h.Remove("Server");
        h.Remove("X-Powered-By");
        await _next(context);
    }
}