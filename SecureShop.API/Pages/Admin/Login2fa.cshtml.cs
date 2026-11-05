using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using SecureShop.Domain.Entities;
using System.Security.Claims;

namespace SecureShop.API.Pages.Admin;

/// <summary>
/// Second step of admin sign-in: verifies the TOTP code (or a recovery code)
/// issued after a successful password check. The pending user id travels in
/// the short-lived TwoFactorUserIdScheme cookie — no AdminCookie is issued
/// until the code verifies.
/// </summary>
[EnableRateLimiting("adminlogin")]
public class AdminTwoFactorLoginModel : PageModel
{
    private static readonly string PendingScheme = IdentityConstants.TwoFactorUserIdScheme;
    private const string GenericError = "Invalid verification code";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AdminTwoFactorLoginModel> _logger;

    public AdminTwoFactorLoginModel(
        UserManager<ApplicationUser> userManager,
        ILogger<AdminTwoFactorLoginModel> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public string? ErrorMessage { get; private set; }

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private async Task<string?> PendingUserIdAsync()
    {
        var result = await HttpContext.AuthenticateAsync(PendingScheme);
        return result.Succeeded
            ? result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await PendingUserIdAsync() is null)
            return Redirect("/admin/login");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string code)
    {
        var userId = await PendingUserIdAsync();
        if (userId is null)
            return Redirect("/admin/login");

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !await _userManager.IsInRoleAsync(user, "Admin"))
        {
            await HttpContext.SignOutAsync(PendingScheme);
            return Redirect("/admin/login");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            ErrorMessage = GenericError;
            return Page();
        }

        // Accept either a TOTP code or a recovery code.
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(user, "Authenticator", code)
            || await _userManager.VerifyTwoFactorTokenAsync(user, "RecoveryCodes", code);

        if (!isValid)
        {
            _logger.LogWarning(
                "[AdminLogin] TOTP verification failed for user {UserId} from {Ip} at {Time:u}",
                user.Id, ClientIp, DateTime.UtcNow);
            ErrorMessage = GenericError;
            return Page();
        }

        await HttpContext.SignOutAsync(PendingScheme);
        await SignInAdminAsync(user);
        _logger.LogInformation(
            "[AdminLogin] Admin TOTP sign-in succeeded for user {UserId} from {Ip} at {Time:u}",
            user.Id, ClientIp, DateTime.UtcNow);
        return Redirect("/admin");
    }

    /// <summary>Establishes the AdminCookie session: non-persistent, 45-minute sliding.</summary>
    private async Task SignInAdminAsync(ApplicationUser user)
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
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(45),
                AllowRefresh = true
            });
    }
}
