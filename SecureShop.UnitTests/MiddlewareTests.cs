using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Moq;
using SecureShop.API.Middleware;
using SecureShop.Domain.Exceptions;
using System.Text.Json;

namespace SecureShop.UnitTests;

// ════════════════════════════════════════════════════════════════════════════
// GlobalExceptionMiddleware
// ════════════════════════════════════════════════════════════════════════════

public class GlobalExceptionMiddlewareTests
{
    private static ILogger<GlobalExceptionMiddleware> MockLogger() =>
        new Mock<ILogger<GlobalExceptionMiddleware>>().Object;

    private static async Task<(int statusCode, JsonDocument body)> InvokeAsync(Exception toThrow)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var next = new RequestDelegate(_ => throw toThrow);
        var mw = new GlobalExceptionMiddleware(next, MockLogger());
        await mw.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, body);
    }

    [Fact]
    public async Task DomainException_Returns400WithMessage()
    {
        var (status, body) = await InvokeAsync(new DomainException("bad input"));

        status.Should().Be(400);
        body.RootElement.GetProperty("message").GetString().Should().Be("bad input");
    }

    [Fact]
    public async Task UnauthorizedAccessException_Returns401()
    {
        var (status, body) = await InvokeAsync(new UnauthorizedAccessException());

        status.Should().Be(401);
        body.RootElement.GetProperty("message").GetString().Should().Be("Unauthorized");
    }

    [Fact]
    public async Task UnexpectedException_Returns500WithGenericMessage()
    {
        var (status, body) = await InvokeAsync(new InvalidOperationException("boom"));

        status.Should().Be(500);
        body.RootElement.GetProperty("message").GetString()
            .Should().Contain("unexpected");
    }

    [Fact]
    public async Task ResponseBody_ContainsStatusField()
    {
        var (_, body) = await InvokeAsync(new DomainException("x"));
        body.RootElement.GetProperty("status").GetInt32().Should().Be(400);
    }

    [Fact]
    public async Task NoException_PassesThrough()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        bool nextCalled = false;
        var next = new RequestDelegate(_ => { nextCalled = true; return Task.CompletedTask; });
        await new GlobalExceptionMiddleware(next, MockLogger()).InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// SecurityHeadersMiddleware
// ════════════════════════════════════════════════════════════════════════════

public class SecurityHeadersMiddlewareTests
{
    private static RequestDelegate NoopNext() =>
        new(_ => Task.CompletedTask);

    private static IWebHostEnvironment Env(string name)
    {
        var mock = new Mock<IWebHostEnvironment>();
        mock.Setup(e => e.EnvironmentName).Returns(name);
        return mock.Object;
    }

    private static async Task<IHeaderDictionary> InvokeAsync(
        string path, IWebHostEnvironment env)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = path;

        var mw = new SecurityHeadersMiddleware(NoopNext(), env);
        await mw.InvokeAsync(context);
        return context.Response.Headers;
    }

    [Theory]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    [InlineData("/api-docs")]
    public async Task SwaggerOrApiDocsPaths_SkipSecurityHeaders(string path)
    {
        var headers = await InvokeAsync(path, Env("Production"));

        headers.ContainsKey("X-Content-Type-Options").Should().BeFalse();
    }

    [Fact]
    public async Task RegularPath_SetsBasicSecurityHeaders()
    {
        var headers = await InvokeAsync("/products", Env("Production"));

        headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        headers["X-XSS-Protection"].ToString().Should().Be("1; mode=block");
        headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
    }

    [Fact]
    public async Task RegularPath_Development_SetsXFrameOptionsSameOrigin()
    {
        var headers = await InvokeAsync("/home", Env("Development"));

        headers["X-Frame-Options"].ToString().Should().Be("SAMEORIGIN");
    }

    [Fact]
    public async Task RegularPath_Production_SetsXFrameOptionsDeny()
    {
        var headers = await InvokeAsync("/home", Env("Production"));

        headers["X-Frame-Options"].ToString().Should().Be("DENY");
    }

    [Fact]
    public async Task RegularPath_Production_CspContainsFrameAncestorsNone()
    {
        var headers = await InvokeAsync("/home", Env("Production"));

        headers["Content-Security-Policy"].ToString().Should().Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task RegularPath_Development_CspContainsFrameAncestorsSelf()
    {
        var headers = await InvokeAsync("/home", Env("Development"));

        headers["Content-Security-Policy"].ToString().Should().Contain("frame-ancestors 'self'");
    }

    [Fact]
    public async Task RegularPath_RemovesServerHeader()
    {
        // Pre-set Server header; middleware should strip it
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/products";
        context.Response.Headers["Server"] = "Kestrel";

        await new SecurityHeadersMiddleware(NoopNext(), Env("Production")).InvokeAsync(context);

        context.Response.Headers.ContainsKey("Server").Should().BeFalse();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// CacheBustingMiddleware
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// DefaultHttpContext does not fire IHttpResponseFeature.OnStarting callbacks when
/// Response.StartAsync() is called (Kestrel normally does this). We swap in a
/// custom IHttpResponseFeature that captures and fires the callbacks on demand.
/// </summary>
internal sealed class CallbackCapturingResponseFeature : IHttpResponseFeature
{
    private readonly List<(Func<object, Task> cb, object state)> _callbacks = new();

    public int StatusCode { get; set; } = 200;
    public string? ReasonPhrase { get; set; }
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
    public Stream Body { get; set; } = Stream.Null;
    public bool HasStarted { get; private set; }

    public void OnStarting(Func<object, Task> callback, object state) =>
        _callbacks.Add((callback, state));

    public void OnCompleted(Func<object, Task> callback, object state) { }

    public async Task FireOnStartingAsync()
    {
        HasStarted = true;
        foreach (var (cb, state) in _callbacks)
            await cb(state);
    }
}

public class CacheBustingMiddlewareTests
{
    private static async Task<IHeaderDictionary> InvokeAsync(string path, int statusCode = 200)
    {
        var context = new DefaultHttpContext();
        var responseFeature = new CallbackCapturingResponseFeature { StatusCode = statusCode };
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        context.Request.Path = path;

        var next = new RequestDelegate(_ => Task.CompletedTask);
        await new CacheBustingMiddleware(next).InvokeAsync(context);

        // Simulate Kestrel firing OnStarting callbacks before writing headers
        await responseFeature.FireOnStartingAsync();

        return responseFeature.Headers;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/docs")]
    [InlineData("/docs/something")]
    [InlineData("/swagger/index.html")]
    [InlineData("/api-docs")]
    [InlineData("/account/login")]
    [InlineData("/openapi.json")]
    [InlineData("/index.html")]
    public async Task MatchingPaths_SetCacheBustingHeaders(string path)
    {
        var headers = await InvokeAsync(path, 200);

        headers["Cache-Control"].ToString().Should().Contain("no-store");
        headers["Pragma"].ToString().Should().Be("no-cache");
        headers["Expires"].ToString().Should().Be("0");
    }

    [Theory]
    [InlineData("/products")]
    [InlineData("/cart")]
    [InlineData("/about")]
    [InlineData("/api/products")]
    public async Task NonMatchingPaths_DoNotSetCacheBustingHeaders(string path)
    {
        var headers = await InvokeAsync(path, 200);

        headers.ContainsKey("Cache-Control").Should().BeFalse();
    }

    [Fact]
    public async Task MatchingPath_StatusCode404_DoesNotSetCacheHeaders()
    {
        var headers = await InvokeAsync("/", 404);

        headers.ContainsKey("Cache-Control").Should().BeFalse();
    }

    [Fact]
    public async Task MatchingPath_StatusCode302_SetsCacheHeaders()
    {
        var headers = await InvokeAsync("/", 302);

        headers["Cache-Control"].ToString().Should().Contain("no-store");
    }
}
