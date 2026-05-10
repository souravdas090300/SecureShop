using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace SecureShop.API.Pages.Admin;

/// <summary>
/// Page model for the admin login page.
/// Authenticates against ASP.NET Core Identity, verifies the Admin role,
/// generates an <c>AdminCookie</c> cookie-based identity from the returned JWT claims,
/// and redirects to the admin dashboard on success.
/// </summary>
public class AdminLoginModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<AdminLoginModel> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public AdminLoginModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<AdminLoginModel> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient();
    }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
        _logger.LogInformation("[AdminLogin] Admin login page accessed");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        _logger.LogInformation("[AdminLogin] Admin login attempt for: {Email}", Email);

        try
        {
            // Find user
            var user = await _userManager.FindByEmailAsync(Email);
            if (user == null)
            {
                _logger.LogWarning("[AdminLogin] User not found: {Email}", Email);
                ErrorMessage = "Invalid admin credentials";
                return Page();
            }

            // Verify password
            var passwordValid = await _userManager.CheckPasswordAsync(user, Password);
            if (!passwordValid)
            {
                _logger.LogWarning("[AdminLogin] Invalid password for: {Email}", Email);
                ErrorMessage = "Invalid admin credentials";
                return Page();
            }

            // Check if user has Admin role
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin)
            {
                _logger.LogWarning("[AdminLogin] User {Email} attempted admin login without Admin role", Email);
                ErrorMessage = "Access denied. Admin privileges required.";
                return Page();
            }

            _logger.LogInformation("[AdminLogin] Admin role verified for: {Email}", Email);

            // Get user roles
            var roles = await _userManager.GetRolesAsync(user);

            // Create claims
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email!),
                new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
                new Claim(ClaimTypes.GivenName, user.FirstName),
                new Claim(ClaimTypes.Surname, user.LastName),
                new Claim("IsAdmin", "true")
            };

            // Add role claims
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            _logger.LogInformation("[AdminLogin] Created {Count} claims for admin user", claims.Count);

            // Create identity and principal with admin cookie scheme
            var identity = new ClaimsIdentity(claims, "AdminCookie");
            var principal = new ClaimsPrincipal(identity);

            // Sign in with admin cookie scheme
            await HttpContext.SignInAsync(
                "AdminCookie",
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
                    AllowRefresh = true
                });

            _logger.LogInformation("[AdminLogin] Admin signed in successfully: {Email}", Email);

            // Issue JWT so JS fetch calls (create/edit/delete product) can authenticate via Bearer
            var jwtSecret = _configuration["Jwt:Secret"];
            if (string.IsNullOrEmpty(jwtSecret))
            {
                _logger.LogError("[AdminLogin] Jwt:Secret is not configured");
                ErrorMessage = "Server configuration error: JWT secret is missing. Contact the administrator.";
                return Page();
            }

            var jwtIssuer   = _configuration["Jwt:Issuer"];
            var jwtAudience = _configuration["Jwt:Audience"];
            if (string.IsNullOrEmpty(jwtIssuer) || string.IsNullOrEmpty(jwtAudience))
            {
                _logger.LogError("[AdminLogin] Jwt:Issuer or Jwt:Audience is not configured");
                ErrorMessage = "Server configuration error: JWT issuer/audience is missing. Contact the administrator.";
                return Page();
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = System.Text.Encoding.UTF8.GetBytes(jwtSecret);
            var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(8),
                Issuer = jwtIssuer,
                Audience = jwtAudience,
                SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                    new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
                    Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var jwtString = tokenHandler.WriteToken(token);

            Response.Cookies.Append("AuthToken", jwtString, new CookieOptions
            {
                HttpOnly = false,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddHours(8),
                Path = "/"
            });

            // Redirect to admin dashboard
            return Redirect("/admin");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AdminLogin] Login failed for {Email}. Type: {ExType} | Message: {ExMsg}",
                Email, ex.GetType().Name, ex.Message);
            ErrorMessage = $"Login failed ({ex.GetType().Name}). Check server logs for details.";
            return Page();
        }
    }
}
