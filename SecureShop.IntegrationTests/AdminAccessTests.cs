using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace SecureShop.IntegrationTests;

/// <summary>
/// End-to-end authorization tests for the /Admin Razor Pages folder.
/// Enumerates every page under Pages/Admin and asserts:
///   anonymous  -> redirect to /admin/login
///   Customer   -> HTTP 403
/// Auth cookies are forged with the app's own IDataProtectionProvider (keys
/// swapped to ephemeral file storage in the factory), so no database or real
/// login is required — authorization runs before handlers.
/// </summary>
// The collection also serializes these DB-less host checks against tests that
// temporarily set the process-wide test connection string.
[Collection("SecureShopDatabase")]
public class AdminAccessTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminCookieName = ".SecureShop.Admin";
    private const string UserCookieName = ".SecureShop.User";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminAccessTests(WebApplicationFactory<Program> factory)
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
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                // Production persists DP keys to Postgres. Use temporary file storage for forged cookies.
                services.RemoveAll<IXmlRepository>();
                services.AddDataProtection()
                    .SetApplicationName("SecureShop.AdminAccessTests")
                    .PersistKeysToFileSystem(new DirectoryInfo(
                        Path.Combine(Path.GetTempPath(), $"dp-keys-{Guid.NewGuid():N}")));
            });
        });
        _ = _factory.Services;
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", previousConnectionString);
            Environment.SetEnvironmentVariable("Testing__DisableStartupDatabaseTasks", previousDisableStartupTasks);
        }
    }

    /// <summary>Anonymous-allowed pages: the login form and the self-guarding 2FA challenge.</summary>
    private static readonly HashSet<string> AnonymousAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "/admin/login",
        "/admin/login2fa"
    };

    public static IEnumerable<object[]> AdminPages()
    {
        var adminDir = FindAdminPagesDirectory();
        foreach (var file in Directory.EnumerateFiles(adminDir, "*.cshtml", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('_') || name.StartsWith('~'))
                continue; // partials / view-starts are not routable pages

            // Honour explicit @page "..." route templates (e.g. edit/{id});
            // substitute route parameters with a dummy GUID.
            var route = ReadPageRoute(file);
            route = System.Text.RegularExpressions.Regex.Replace(
                route, @"\{[^}]+\}", "00000000-0000-0000-0000-000000000000");
            yield return new object[] { route };
        }
    }

    private static string FindAdminPagesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "SecureShop.API", "Pages", "Admin");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate SecureShop.API/Pages/Admin from test base directory.");
    }

    private static string ReadPageRoute(string cshtmlPath)
    {
        var adminDir = FindAdminPagesDirectory();
        var relative = Path.GetRelativePath(adminDir, cshtmlPath);
        var fallback = "/admin/" + relative[..^".cshtml".Length]
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        if (fallback.EndsWith("/Index", StringComparison.OrdinalIgnoreCase))
            fallback = fallback[..^"/Index".Length];
        if (fallback.EndsWith('/'))
            fallback = fallback[..^1];

        foreach (var line in File.ReadLines(cshtmlPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("@page", StringComparison.Ordinal))
                continue;
            var rest = trimmed["@page".Length..].Trim();
            if (rest.StartsWith('"') && rest.EndsWith('"') && rest.Length >= 2)
            {
                var template = rest[1..^1].Trim();
                // Absolute templates already include the /admin prefix.
                return template.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
                    ? template
                    : "/admin/" + template.TrimStart('/');
            }
            return fallback; // bare @page -> folder route
        }
        return fallback;
    }

    private string ForgeCookie(string cookieName, string scheme, params Claim[] claims)
    {
        var provider = _factory.Services.GetRequiredService<IDataProtectionProvider>();
        // CookieAuthenticationHandler builds its TicketDataFormat protector with
        // the SCHEME name (not the cookie name) as the second purpose.
        var protector = provider.CreateProtector(
            "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware",
            scheme, "v2");
        var identity = new ClaimsIdentity(claims, scheme);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties(),
            scheme);
        var bytes = TicketSerializer.Default.Serialize(ticket);
        return Convert.ToBase64String(protector.Protect(bytes));
    }

    private static Claim[] CustomerClaims() =>
    [
        new(ClaimTypes.NameIdentifier, "customer-1"),
        new(ClaimTypes.Name, "Test Customer"),
        new(ClaimTypes.Role, "Customer")
    ];

    private static Claim[] AdminClaims() =>
    [
        new(ClaimTypes.NameIdentifier, "admin-1"),
        new(ClaimTypes.Name, "Test Admin"),
        new(ClaimTypes.Role, "Admin")
    ];

    private HttpClient ClientWithCookie(string name, string value)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("Cookie", $"{name}={value}");
        return client;
    }

    private static bool IsAdminLoginRedirect(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Redirect
        && (response.Headers.Location?.ToString() ?? string.Empty)
            .Contains("/admin/login", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [MemberData(nameof(AdminPages))]
    public async Task Anonymous_IsRedirectedToAdminLogin(string route)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync(route);

        if (AnonymousAllowed.Contains(route))
        {
            // Login page renders; the 2FA challenge self-redirects without a
            // pending TwoFactorUserIdScheme cookie.
            Assert.True(
                response.StatusCode == HttpStatusCode.OK || IsAdminLoginRedirect(response),
                $"{route} should be anonymously reachable, got {(int)response.StatusCode}");
        }
        else
        {
            Assert.True(IsAdminLoginRedirect(response),
                $"{route} expected redirect to /admin/login, got {(int)response.StatusCode} loc={response.Headers.Location}");
        }
    }

    [Theory]
    [MemberData(nameof(AdminPages))]
    public async Task Customer_GetsForbidden(string route)
    {
        if (AnonymousAllowed.Contains(route))
            return; // login pages are intentionally reachable by anyone

        var cookie = ForgeCookie(UserCookieName, CookieAuthenticationDefaults.AuthenticationScheme, CustomerClaims());
        var client = ClientWithCookie(UserCookieName, cookie);

        var response = await client.GetAsync(route);

        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            $"{route} expected 403, got {(int)response.StatusCode} loc={response.Headers.Location}");
    }

    [Theory]
    [MemberData(nameof(AdminPages))]
    public async Task Admin_IsNotRedirectedOrForbidden(string route)
    {
        // /admin/logout GET intentionally redirects to the login page (sign-out
        // is POST-only) — authorization is still enforced before the handler.
        if (AnonymousAllowed.Contains(route) || route.Equals("/admin/logout", StringComparison.OrdinalIgnoreCase))
            return;

        var cookie = ForgeCookie(AdminCookieName, "AdminCookie", AdminClaims());
        var client = ClientWithCookie(AdminCookieName, cookie);

        var response = await client.GetAsync(route);

        // Authorization passed if we did NOT get bounced to login or 403.
        // Downstream handlers may still fail (no real DB), which is fine.
        Assert.False(IsAdminLoginRedirect(response), $"{route} redirected an admin to login");
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminIdentity_DoesNotGrantCustomerAreaAccess()
    {
        // An admin signed in ONLY via AdminCookie must not satisfy the default
        // (customer) cookie scheme used by /account pages.
        var cookie = ForgeCookie(AdminCookieName, "AdminCookie", AdminClaims());
        var client = ClientWithCookie(AdminCookieName, cookie);

        var response = await client.GetAsync("/account/orders");

        // Unauthenticated under the customer scheme -> redirect to customer login.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task CustomerIdentity_DoesNotGrantAdminApiAccess()
    {
        // A customer authenticated via the default cookie must not satisfy the
        // AdminApiPolicy (JWT Bearer + Admin role) on /api/admin endpoints.
        var cookie = ForgeCookie(UserCookieName, CookieAuthenticationDefaults.AuthenticationScheme, CustomerClaims());
        var client = ClientWithCookie(UserCookieName, cookie);

        var response = await client.GetAsync("/api/admin/seed");

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed,
            $"Expected 401/403/405, got {(int)response.StatusCode}");
    }

    [Fact]
    public void AdminCookie_IsNotSentOutsideAdminPath()
    {
        // Cookie Path=/admin means the browser never attaches the admin cookie
        // to storefront/account requests — verified at the options level.
        var monitor = _factory.Services.GetRequiredService<
            Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>();
        var admin = monitor.Get("AdminCookie");
        var user = monitor.Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal("/admin", admin.Cookie.Path);
        Assert.NotEqual(admin.Cookie.Name, user.Cookie.Name);
        Assert.Equal(SameSiteMode.Strict, admin.Cookie.SameSite);
        Assert.True(admin.Cookie.HttpOnly);
        Assert.Equal(TimeSpan.FromMinutes(45), admin.ExpireTimeSpan);
        Assert.True(admin.SlidingExpiration);
    }

    [Fact]
    public async Task AdminPages_SendNoIndexAndNoStore()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/admin/login");

        var dump = $"status={(int)response.StatusCode} loc={response.Headers.Location} " +
                   string.Join(";", response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"));
        Assert.True(response.Headers.TryGetValues("X-Robots-Tag", out var robots), dump);
        Assert.Equal("noindex, nofollow", robots!.Single());
        Assert.True(response.Headers.TryGetValues("Cache-Control", out var cc), dump);
        Assert.Contains(cc!, v => v.Contains("no-store", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Storefront_DoesNotSendAdminHeaders()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/");

        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

}
