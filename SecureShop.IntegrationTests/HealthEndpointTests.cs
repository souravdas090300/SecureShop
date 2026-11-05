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

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
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
        _client = _factory.CreateClient();
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

        foreach (var scheme in new[] { CookieAuthenticationDefaults.AuthenticationScheme, "AdminCookie" })
        {
            var cookie = options.Get(scheme).Cookie;
            Assert.Equal(CookieSecurePolicy.Always, cookie.SecurePolicy);
            Assert.Equal(SameSiteMode.Lax, cookie.SameSite);
        }
    }
}
