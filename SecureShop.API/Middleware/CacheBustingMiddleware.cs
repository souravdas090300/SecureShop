namespace SecureShop.API.Middleware;

public class CacheBustingMiddleware
{
    private readonly RequestDelegate _next;

    public CacheBustingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        
        // Check if this is a Swagger/API doc path
        if (path == "/" || path.StartsWith("/swagger") || path.StartsWith("/api-docs") || path.Contains(".json"))
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
