namespace SecureShop.API.Middleware;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var h = context.Response.Headers;
        h.Append("X-Content-Type-Options", "nosniff");
        h.Append("X-Frame-Options", "DENY");
        h.Append("X-XSS-Protection", "1; mode=block");
        h.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        h.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
        h.Append("Content-Security-Policy", "default-src 'self'; frame-ancestors 'none';");
        h.Remove("Server");
        h.Remove("X-Powered-By");
        await _next(context);
    }
}