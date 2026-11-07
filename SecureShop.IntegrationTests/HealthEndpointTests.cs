using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SecureShop.IntegrationTests;

// All integration test classes share one collection: they mutate the
// process-wide ConnectionStrings__DefaultConnection env var while building
// their hosts, so parallel collections can hand a factory the wrong
// connection string.
[Collection("SecureShopDatabase")]
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        // appsettings.json ships an empty DefaultConnection that wins over
        // in-memory config, so the Production DB guard needs the env var.
        // Restored as soon as the host has consumed it.
        var previous = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Host=unused.invalid;Database=x;Username=u;Password=p;SSL Mode=Require");
        try
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:Secret"] = "integration-test-secret-that-is-long-enough",
                        ["Jwt:Issuer"] = "SecureShop.Tests",
                        ["Jwt:Audience"] = "SecureShop.Tests"
                    }));
            });
            _client = _factory.CreateClient(); // force host build so the env var is consumed
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", previous);
        }
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Production_SwaggerIsUnavailableAndSecurityHeadersAllowStripePaymentElement()
    {
        var response = await _client.GetAsync("/swagger/index.html");
        var healthResponse = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(healthResponse.Headers.TryGetValues("Content-Security-Policy", out var values));
        var policy = Assert.Single(values!);
        Assert.Contains("https://js.stripe.com", policy);
        Assert.Contains("https://api.stripe.com", policy);
        Assert.DoesNotContain("X-XSS-Protection", healthResponse.Headers.Select(header => header.Key));
    }

    [Fact]
    public async Task Products_RejectsUnboundedPageSize()
    {
        var response = await _client.GetAsync("/api/products?pageSize=1000000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/authtest")]
    [InlineData("/account/authtest")]
    public async Task Production_AuthenticationDebugPagesAreUnavailable(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void AuthenticationCookies_AreSecureAndLaxInProduction()
    {
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        var userCookie = options.Get(CookieAuthenticationDefaults.AuthenticationScheme).Cookie;
        Assert.Equal(CookieSecurePolicy.Always, userCookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, userCookie.SameSite);

        // Admin cookie is deliberately stricter: SameSite=Strict, Path=/admin.
        var adminCookie = options.Get("AdminCookie").Cookie;
        Assert.Equal(CookieSecurePolicy.Always, adminCookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Strict, adminCookie.SameSite);
        Assert.Equal("/admin", adminCookie.Path);
    }
}
