using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using SecureShop.API.Helpers;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;

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
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;

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

    public RegisterModel(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<RegisterModel> logger,
        UserManager<ApplicationUser> userManager, IEmailService emailService)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _userManager = userManager;
        _emailService = emailService;
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
            var baseUrl = $"http://localhost:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}";

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
                    // Account created — send OTP to verify email before logging in.
                    var user = await _userManager.FindByEmailAsync(Email);
                    if (user != null)
                    {
                        var otp     = Random.Shared.Next(100_000, 999_999).ToString();
                        var expiry  = DateTime.UtcNow.AddMinutes(15).ToString("O");
                        await _userManager.SetAuthenticationTokenAsync(
                            user, "SecureShop", "EmailVerifyOTP", $"{otp}|{expiry}");

                        try
                        {
                            await _emailService.SendAsync(
                                Email,
                                "Your SecureShop verification code",
                                BuildOtpEmail(result.FirstName ?? FirstName, otp));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "OTP email failed for {Email}", Email);
                            // Continue — user can request a resend later.
                        }

                        TempData["PendingEmail"]   = Email;
                        TempData["PendingUserId"]  = user.Id;
                        TempData["PendingToken"]   = result.Token;
                        TempData["SuccessMessage"] = $"Welcome {result.FirstName ?? FirstName}! A 6-digit verification code has been sent to {Email}.";
                        return RedirectToPage("/Account/VerifyEmail");
                    }

                    // Fallback (user not found after creation — very unlikely)
                    TempData["SuccessMessage"] = "Registration successful! Please sign in.";
                    return RedirectToPage("/Account/Login");
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

    private static string BuildOtpEmail(string firstName, string otp) => $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family:Arial,sans-serif;background:#f4f4f4;padding:20px;">
          <div style="max-width:480px;margin:auto;background:#fff;border-radius:10px;padding:40px;">
            <h2 style="color:#2563eb;">Verify your email</h2>
            <p>Hi {System.Net.WebUtility.HtmlEncode(firstName)},</p>
            <p>Use the code below to complete your SecureShop sign-up. It expires in <strong>15 minutes</strong>.</p>
            <div style="font-size:40px;font-weight:bold;letter-spacing:12px;text-align:center;
                        background:#f1f5f9;border-radius:8px;padding:20px 0;margin:24px 0;color:#1e293b;">
              {otp}
            </div>
            <p style="color:#64748b;font-size:0.875rem;">If you didn't create an account, you can ignore this email.</p>
          </div>
        </body>
        </html>
        """;

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
