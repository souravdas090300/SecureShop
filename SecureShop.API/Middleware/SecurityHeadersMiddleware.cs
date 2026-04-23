namespace SecureShop.API.Middleware;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _environment;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

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
            h["X-Frame-Options"] = "SAMEORIGIN";
            h["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://accounts.google.com https://code.jquery.com; " +
                "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://accounts.google.com; " +
                "img-src 'self' data: https:; " +
                "font-src 'self' data: https://cdnjs.cloudflare.com; " +
                "connect-src 'self' https://localhost:5001 http://localhost:5000 http://localhost:8080 https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://accounts.google.com; " +
                "frame-ancestors 'self';";
        }
        else
        {
            // Production CSP - Allow CDN resources for Bootstrap, Font Awesome, jQuery, and Google OAuth
            h["X-Frame-Options"] = "DENY";
            h["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://accounts.google.com https://code.jquery.com; " +
                "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://accounts.google.com; " +
                "img-src 'self' data: https:; " +
                "font-src 'self' data: https://cdnjs.cloudflare.com; " +
                "connect-src 'self' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://accounts.google.com; " +
                "base-uri 'self'; " +
                "form-action 'self'; " +
                "frame-ancestors 'none';";
        }

        h.Remove("Server");
        h.Remove("X-Powered-By");
        await _next(context);
    }
}