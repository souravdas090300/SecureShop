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

public class LoginModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

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
    public string GoogleClientId => _configuration["GoogleAuth:ClientId"] ?? "";

    public LoginModel(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public void OnGet(string? returnUrl = null)
    {
        // Check if user just registered
        if (TempData["SuccessMessage"] != null)
        {
            SuccessMessage = TempData["SuccessMessage"]?.ToString();
        }
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= "/";
        
        Console.WriteLine($"[Login Page] OnPostAsync called - Email: '{Email}'");

        if (!ModelState.IsValid)
        {
            Console.WriteLine($"[Login Page] ModelState is INVALID");
            return Page();
        }
        
        Console.WriteLine($"[Login Page] ModelState is valid, calling login API");

        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var loginData = new
            {
                email = Email,
                password = Password
            };

            var json = JsonSerializer.Serialize(loginData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{baseUrl}/api/auth/login", content);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<AuthResponse>(responseJson, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (result?.Token != null)
                {
                    // Store token in cookie
                    var cookieOptions = new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Expires = RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8)
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
                            IsPersistent = RememberMe,
                            ExpiresUtc = RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8)
                        });

                    return Redirect(returnUrl);
                }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                ErrorMessage = "Invalid email or password. Please try again.";
            }
            else
            {
                ErrorMessage = "An error occurred during login. Please try again later.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "Unable to connect to the server. Please try again later.";
            Console.WriteLine($"Login error: {ex.Message}");
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
}
