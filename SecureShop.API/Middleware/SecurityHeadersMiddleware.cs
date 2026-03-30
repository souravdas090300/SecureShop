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
        // Skip security headers for Swagger endpoints and static files
        var path = context.Request.Path.Value?.ToLower() ?? "";
        if (path.StartsWith("/swagger") || path.StartsWith("/swagger-ui.css") || 
            path.StartsWith("/favicon") || path.StartsWith("/.") || path == "/")
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
                "script-src 'self' 'unsafe-inline'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; " +
                "font-src 'self' data:; " +
                "connect-src 'self' https://localhost:5001 http://localhost:5000; " +
                "frame-ancestors 'self' http://localhost:3000 https://localhost:3001;";
        }
        else
        {
            h["X-Frame-Options"] = "DENY";
            h["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self'; " +
                "style-src 'self'; " +
                "img-src 'self' data:; " +
                "font-src 'self'; " +
                "base-uri 'self'; " +
                "form-action 'self'; " +
                "frame-ancestors 'none';";
        }

        h.Remove("Server");
        h.Remove("X-Powered-By");
        await _next(context);
    }
}