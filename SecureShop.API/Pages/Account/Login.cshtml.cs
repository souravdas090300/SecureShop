using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using SecureShop.API.Helpers;

namespace SecureShop.API.Pages.Account;

/// <summary>
/// Page model for the customer login page.
/// Submits credentials to the internal Auth API, receives a JWT, and converts
/// it into an ASP.NET Core cookie identity so Razor Pages can use <c>[Authorize]</c>.
/// Also exposes the Google Client ID needed by the Google Sign-In button.
/// </summary>
public class LoginModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LoginModel> _logger;

    [BindProperty]
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public bool RememberMe { get; set; }

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    /// <summary>The Google OAuth client ID, injected from configuration for the front-end Sign-In button.</summary>
    public string GoogleClientId => _configuration["GoogleAuth:ClientId"] ?? "";

    /// <summary>Injects the HTTP client factory, application configuration, and structured logger.</summary>
    public LoginModel(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<LoginModel> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public void OnGet(string? returnUrl = null)
    {
        if (TempData["SuccessMessage"] != null)
            SuccessMessage = TempData["SuccessMessage"]?.ToString();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= "/";

        if (!ModelState.IsValid)
            return Page();

        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var loginData = new { email = Email, password = Password };
            var json = JsonSerializer.Serialize(loginData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{baseUrl}/api/auth/login", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<AuthResponse>(responseBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result?.Token != null)
                {
                    // Store raw JWT in a cookie so other page models (e.g. OrdersModel)
                    // can attach it as a Bearer token when calling the internal API.
                    var cookieOptions = new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Expires = RememberMe
                            ? DateTimeOffset.UtcNow.AddDays(30)
                            : DateTimeOffset.UtcNow.AddHours(8)
                    };
                    Response.Cookies.Append("AuthToken", result.Token, cookieOptions);

                    // Build a cookie identity with standard ClaimTypes from the JWT.
                    var principal = JwtCookieHelper.BuildPrincipal(
                        result.Token,
                        overrideEmail:     result.Email,
                        overrideFirstName: result.FirstName,
                        overrideLastName:  result.LastName);

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        principal,
                        new AuthenticationProperties
                        {
                            IsPersistent = true,
                            ExpiresUtc = RememberMe
                                ? DateTimeOffset.UtcNow.AddDays(30)
                                : DateTimeOffset.UtcNow.AddHours(8),
                            AllowRefresh = true
                        });

                    _logger.LogInformation("Login successful for {Email}, redirecting to {ReturnUrl}", Email, returnUrl);
                    return Redirect(returnUrl);
                }

                ErrorMessage = "Login successful but authentication failed. Please try again.";
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                ErrorMessage = "Invalid email or password. Please check your credentials and try again.";
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                ErrorMessage = "Invalid login request. Please check your information.";
            }
            else
            {
                _logger.LogWarning("Login API returned {StatusCode} for {Email}", response.StatusCode, Email);
                ErrorMessage = $"An error occurred during login ({response.StatusCode}). Please try again.";
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error during login for {Email}", Email);
            ErrorMessage = "Unable to connect to the authentication server. Please check your connection.";
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parse error during login for {Email}", Email);
            ErrorMessage = "Received invalid response from server. Please try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during login for {Email}", Email);
            ErrorMessage = "An unexpected error occurred. Please try again.";
        }

        return Page();
    }

    private sealed class AuthResponse
    {
        public string? Token { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }
}
