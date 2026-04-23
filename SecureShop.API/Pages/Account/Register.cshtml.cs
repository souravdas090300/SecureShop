using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SecureShop.API.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

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

    public RegisterModel(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Console.WriteLine($"[Register Page] OnPostAsync called - Email: '{Email}', FirstName: '{FirstName}', LastName: '{LastName}', Password Length: {Password?.Length ?? 0}");
        
        if (!ModelState.IsValid)
        {
            Console.WriteLine($"[Register Page] ModelState is INVALID");
            var errors = new List<string>();
            foreach (var error in ModelState)
            {
                var errorMessages = string.Join(", ", error.Value?.Errors.Select(e => e.ErrorMessage) ?? Array.Empty<string>());
                if (!string.IsNullOrEmpty(errorMessages))
                {
                    Console.WriteLine($"  - {error.Key}: {errorMessages}");
                    errors.Add($"{error.Key}: {errorMessages}");
                }
            }
            ErrorMessage = "Please fix the following errors: " + string.Join("; ", errors);
            return Page();
        }

        Console.WriteLine($"[Register Page] ModelState is valid, calling registration API");

        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = _configuration["ApiBaseUrl"] ?? "http://localhost:8080";

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
                    // Auto-login after successful registration
                    var cookieOptions = new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Expires = DateTimeOffset.UtcNow.AddHours(8)
                    };
                    Response.Cookies.Append("AuthToken", result.Token, cookieOptions);
                    Response.Cookies.Append("UserEmail", result.Email ?? "", cookieOptions);
                    Response.Cookies.Append("UserName", result.FirstName ?? "", cookieOptions);

                    // Decode JWT token to get claims
                    var handler = new JwtSecurityTokenHandler();
                    var jwtToken = handler.ReadJwtToken(result.Token);
                    
                    // Create claims identity from JWT
                    var claims = jwtToken.Claims.ToList();
                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

                    // Sign in the user with Cookie Authentication
                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        claimsPrincipal,
                        new AuthenticationProperties
                        {
                            IsPersistent = false,
                            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                        });

                    TempData["SuccessMessage"] = "Registration successful! Welcome to SecureShop.";
                    return RedirectToPage("/Index");
                }
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
                catch
                {
                    ErrorMessage = "This email is already registered. Please use a different email or try logging in.";
                }
            }
            else
            {
                ErrorMessage = "An error occurred during registration. Please try again later.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "Unable to connect to the server. Please try again later.";
            Console.WriteLine($"Registration error: {ex.Message}");
        }

        return Page();
    }

    private class AuthResponse
    {
        public string? Token { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }

    private class ErrorResponse
    {
        public string? Message { get; set; }
    }
}
