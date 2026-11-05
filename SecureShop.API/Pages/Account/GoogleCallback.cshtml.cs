using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.API.Helpers;

namespace SecureShop.API.Pages.Account;

/// <summary>
/// Page model for the Google OAuth callback page.
/// Receives the JWT (issued by the server's Google sign-in endpoint) in a POST body,
/// stores it in an <c>AuthToken</c> cookie, builds a cookie identity
/// from the JWT claims using <see cref="JwtCookieHelper"/>, and signs in with the
/// default cookie scheme.
/// Using GET avoids cross-origin issues that arise with POST redirects from the
/// Google OAuth flow.
/// </summary>
public class GoogleCallbackModel : PageModel
{
    private readonly ILogger<GoogleCallbackModel> _logger;
    private readonly IWebHostEnvironment _environment;

    public GoogleCallbackModel(ILogger<GoogleCallbackModel> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public IActionResult OnGet() => RedirectToPage("/Account/Login");

    public async Task<IActionResult> OnPostAsync([FromForm] string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("[GoogleCallback] No token provided, redirecting to login");
            return RedirectToPage("/Account/Login");
        }

        try
        {
            var cookieOptions = AuthTokenCookieHelper.Create(
                HttpContext,
                _environment,
                DateTimeOffset.UtcNow.AddHours(8));
            Response.Cookies.Append("AuthToken", token, cookieOptions);

            // Build a properly-mapped ClaimsPrincipal using the shared helper.
            var principal = JwtCookieHelper.BuildPrincipal(token);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
                    AllowRefresh = true
                });

            _logger.LogInformation("[GoogleCallback] Sign-in successful");
            return Redirect("/");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleCallback] Error processing Google OAuth callback");
            return RedirectToPage("/Account/Login");
        }
    }
}
