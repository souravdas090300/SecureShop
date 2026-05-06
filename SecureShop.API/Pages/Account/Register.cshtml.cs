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
/// Page model for the new user registration page.
/// Submits the registration form to the internal Auth API and sets an auth cookie
/// on success so the user is immediately logged in after registering.
/// </summary>
public class RegisterModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RegisterModel> _logger;

    [BindProperty]
    [Required(ErrorMessage = "First name is required")]
    [StringLength(50, ErrorMessage = "First name cannot be longer than 50 characters")]
    public string FirstName { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Last name is required")]
    [StringLength(50, ErrorMessage = "Last name cannot be longer than 50 characters")]
    public string LastName { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long")]
    [DataType(DataType.Password)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
        ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one number, and one special character")]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please confirm your password")]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
    public string GoogleClientId => _configuration["GoogleAuth:ClientId"] ?? "";

    public RegisterModel(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<RegisterModel> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = "Please fix the following errors: " +
                string.Join("; ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));
            return Page();
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var registerData = new
            {
                email = Email,
                password = Password,
                firstName = FirstName,
                lastName = LastName
            };

            var json = JsonSerializer.Serialize(registerData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{baseUrl}/api/auth/register", content);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<AuthResponse>(responseJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result?.Token != null)
                {
                    // Store raw JWT for API calls made by other page models.
                    var cookieOptions = new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Expires = DateTimeOffset.UtcNow.AddHours(8)
                    };
                    Response.Cookies.Append("AuthToken", result.Token, cookieOptions);

                    // Build cookie identity with standard ClaimTypes.
                    var principal = JwtCookieHelper.BuildPrincipal(
                        result.Token,
                        overrideEmail:     result.Email     ?? Email,
                        overrideFirstName: result.FirstName ?? FirstName,
                        overrideLastName:  result.LastName  ?? LastName);

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        principal,
                        new AuthenticationProperties
                        {
                            IsPersistent = true,
                            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
                            AllowRefresh = true
                        });

                    _logger.LogInformation("Registration and auto-login successful for {Email}", Email);
                    TempData["SuccessMessage"] = $"Welcome {result.FirstName ?? FirstName}! Your account has been created successfully.";
                    return Redirect("/");
                }

                // Registered but no token — shouldn't happen; send to login as fallback.
                TempData["SuccessMessage"] = "Registration successful! Please sign in with your new account.";
                return RedirectToPage("/Account/Login");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    ErrorMessage = errorResponse?.Message ?? "Registration failed. Please check your information.";
                }
                catch (JsonException)
                {
                    ErrorMessage = "This email is already registered. Please use a different email or try logging in.";
                }
            }
            else
            {
                _logger.LogWarning("Registration API returned {StatusCode} for {Email}", response.StatusCode, Email);
                ErrorMessage = "An error occurred during registration. Please try again later.";
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error during registration for {Email}", Email);
            ErrorMessage = "Unable to connect to the authentication server. Please check your connection.";
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parse error during registration for {Email}", Email);
            ErrorMessage = "Received invalid response from server. Please try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during registration for {Email}", Email);
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

    private sealed class ErrorResponse
    {
        public string? Message { get; set; }
    }
}
