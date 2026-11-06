using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using SecureShop.Domain.Entities;
using System.Security.Claims;

namespace SecureShop.API.Pages.Admin;

/// <summary>
/// Page model for the admin login page.
/// Authenticates against ASP.NET Core Identity, verifies the Admin role,
/// and establishes the protected <c>AdminCookie</c> session. When the account
/// has TOTP two-factor enabled, password success redirects to the
/// <c>/admin/login-2fa</c> challenge instead of signing in.
/// All failures return the same generic message and are logged with timestamp
/// and client IP (never credentials).
/// </summary>
[EnableRateLimiting("adminlogin")]
public class AdminLoginModel : PageModel
{
    private const string GenericError = "Invalid admin credentials";
    private static readonly TimeSpan AdminSessionLifetime = TimeSpan.FromMinutes(45);

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

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public void OnGet()
    {
        _logger.LogInformation("[AdminLogin] Admin login page accessed from {Ip} at {Time:u}", ClientIp, DateTime.UtcNow);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(Email);
            if (user == null)
            {
                // Equalize timing with the wrong-password path: verify the
                // supplied password against a throwaway hash so unknown-email
                // attempts don't return measurably faster.
                var dummy = new ApplicationUser
                {
                    UserName = "__dummy__",
                    Email = "__dummy__",
                    PasswordHash = "AQAAAAIAAYagAAAAELxYzX7z1Z2R3AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
                };
                await _userManager.CheckPasswordAsync(dummy, Password);
                LogFailure("unknown account");
                ErrorMessage = GenericError;
                return Page();
            }

            if (!await _userManager.GetLockoutEnabledAsync(user))
            {
                var enableLockout = await _userManager.SetLockoutEnabledAsync(user, true);
                if (!enableLockout.Succeeded)
                    throw new InvalidOperationException("Could not enable account lockout.");
            }

            var passwordResult = await _signInManager.CheckPasswordSignInAsync(
                user, Password, lockoutOnFailure: true);
            if (!passwordResult.Succeeded)
            {
                LogFailure($"invalid credentials (locked out: {passwordResult.IsLockedOut})");
                ErrorMessage = GenericError;
                return Page();
            }

            // Admin role gate — non-admins get the same generic failure.
            if (!await _userManager.IsInRoleAsync(user, "Admin"))
            {
                LogFailure("non-admin account");
                ErrorMessage = GenericError;
                return Page();
            }

            // Two-factor challenge: password verified but TOTP still required.
            if (await _userManager.GetTwoFactorEnabledAsync(user))
            {
                await HttpContext.SignInAsync(
                    IdentityConstants.TwoFactorUserIdScheme,
                    new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, user.Id)],
                        IdentityConstants.TwoFactorUserIdScheme)));
                _logger.LogInformation(
                    "[AdminLogin] Admin {UserId} passed password check; awaiting TOTP from {Ip} at {Time:u}",
                    user.Id, ClientIp, DateTime.UtcNow);
                return Redirect("/admin/login-2fa");
            }

            await SignInAdminAsync(user);
            return Redirect("/admin");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AdminLogin] Internal error during admin sign-in from {Ip} at {Time:u}", ClientIp, DateTime.UtcNow);

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

    /// <summary>Establishes the AdminCookie session: non-persistent, 45-minute sliding.</summary>
    internal async Task SignInAdminAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email!),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
            new("IsAdmin", "true")
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "AdminCookie"));
        await HttpContext.SignInAsync(
            "AdminCookie",
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(AdminSessionLifetime),
                AllowRefresh = true
            });

        _logger.LogInformation(
            "[AdminLogin] Admin sign-in succeeded for user {UserId} from {Ip} at {Time:u}",
            user.Id, ClientIp, DateTime.UtcNow);
    }

    private void LogFailure(string reason) =>
        _logger.LogWarning(
            "[AdminLogin] Admin sign-in failed ({Reason}) for {Email} from {Ip} at {Time:u}",
            reason, Email, ClientIp, DateTime.UtcNow);
}
