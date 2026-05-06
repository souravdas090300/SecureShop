using System.Net;
using System.Text.Json;
using SecureShop.Domain.Exceptions;

namespace SecureShop.API.Middleware;

/// <summary>
/// ASP.NET Core middleware that catches any unhandled exception propagated
/// through the request pipeline and converts it to a structured JSON error response.
/// <para>
/// Mapping rules:
/// <list type="bullet">
///   <item><see cref="DomainException"/> → 400 Bad Request (business rule violation).</item>
///   <item><see cref="UnauthorizedAccessException"/> → 401 Unauthorized.</item>
///   <item>All other exceptions → 500 Internal Server Error (generic message, no stack trace).</item>
/// </list>
/// </para>
/// Register with <c>app.UseMiddleware&lt;GlobalExceptionMiddleware&gt;()</c> early in the pipeline.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    /// <summary>Initialises the middleware with the next delegate and a logger.</summary>
    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the next middleware in the pipeline and handles any unhandled exceptions.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // Log the full exception including stack trace for debugging.
            _logger.LogError(ex, "Unhandled exception at {Path}: {Message} | StackTrace: {StackTrace}",
                context.Request.Path, ex.Message, ex.StackTrace);
            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// Maps the exception to an HTTP status code and writes a JSON error body.
    /// The response includes a machine-readable status code, a human-readable message,
    /// and the ASP.NET Core trace identifier for log correlation.
    /// </summary>
    private static async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        // Pattern-match the exception type to determine the appropriate HTTP status.
        var (statusCode, message) = ex switch
        {
            DomainException => (HttpStatusCode.BadRequest, ex.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized"),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred")
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = (int)statusCode,
            message,
            // Expose the trace ID so support teams can correlate API errors with server logs.
            traceId = context.TraceIdentifier
        }));
    }
}
