using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.Domain.Entities;
using System.Security.Claims;

namespace SecureShop.API.Pages.Admin;

/// <summary>
/// Page model for the admin login page.
/// Authenticates against ASP.NET Core Identity, verifies the Admin role,
/// and establishes the protected <c>AdminCookie</c> session.
/// </summary>
public class AdminLoginModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<AdminLoginModel> _logger;

    public AdminLoginModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<AdminLoginModel> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
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
        try
        {
            var user = await _userManager.FindByEmailAsync(Email);
            if (user == null)
            {
                _logger.LogWarning("Admin login failed because the account was not found");
                ErrorMessage = "Invalid admin credentials";
                return Page();
            }

            if (!await _userManager.GetLockoutEnabledAsync(user))
            {
                var enableLockout = await _userManager.SetLockoutEnabledAsync(user, true);
                if (!enableLockout.Succeeded)
                    throw new InvalidOperationException("Could not enable account lockout.");
            }

            var passwordResult = await _signInManager.CheckPasswordSignInAsync(
                user,
                Password,
                lockoutOnFailure: true);
            if (!passwordResult.Succeeded)
            {
                _logger.LogWarning("Admin login failed because credentials were invalid or locked out");
                ErrorMessage = "Invalid admin credentials";
                return Page();
            }

            // Check if user has Admin role
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin)
            {
                _logger.LogWarning("A non-admin account attempted to access the admin login");
                ErrorMessage = "Invalid admin credentials";
                return Page();
            }

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

            _logger.LogInformation("Admin login succeeded for user {UserId}", user.Id);
            return Redirect("/admin");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin login failed due to an internal error");

            // SocketException (or Npgsql wrapping one) means the DB server is temporarily unreachable.
            // On Neon free tier this happens after the instance auto-pauses.
            bool isSocketError = ex is System.Net.Sockets.SocketException
                || ex.InnerException is System.Net.Sockets.SocketException
                || ex.Message.Contains("socket", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase);

            ErrorMessage = isSocketError
                ? "The authentication service is temporarily unavailable. Please try again shortly."
                : "Unable to sign in right now. Please try again later.";

            return Page();
        }
    }
}
