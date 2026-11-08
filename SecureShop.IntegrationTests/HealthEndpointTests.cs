using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SecureShop.API.Services;

namespace SecureShop.IntegrationTests;

// The collection also serializes these DB-less host checks against tests that
// temporarily set the process-wide test connection string.
[Collection("SecureShopDatabase")]
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        var previousConnectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        var previousDisableStartupTasks =
            Environment.GetEnvironmentVariable("Testing__DisableStartupDatabaseTasks");
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Host=localhost;Port=5432;Database=secureshop_test;Username=test;Password=test");
        Environment.SetEnvironmentVariable("Testing__DisableStartupDatabaseTasks", "true");
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
                    ["Jwt:Audience"] = "SecureShop.Tests",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Port=5432;Database=secureshop_test;Username=test;Password=test",
                    ["Testing:DisableStartupDatabaseTasks"] = "true"
                }));
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        });
        _client = _factory.CreateClient();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", previousConnectionString);
            Environment.SetEnvironmentVariable("Testing__DisableStartupDatabaseTasks", previousDisableStartupTasks);
        }
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void DbLessHost_DisablesStartupDatabaseTasksAndAbandonmentService()
    {
        var migrationSignal = _factory.Services.GetRequiredService<MigrationCompletedSignal>();
        var hostedServices = _factory.Services.GetServices<IHostedService>();

        Assert.True(migrationSignal.Completion.IsCompleted);
        Assert.DoesNotContain(hostedServices, service => service is OrderAbandonmentService);
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
