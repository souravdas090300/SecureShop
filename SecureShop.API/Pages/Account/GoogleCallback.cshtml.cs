using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.API.Helpers;

namespace SecureShop.API.Pages.Account;

/// <summary>
/// Page model for the Google OAuth callback page.
/// Receives the JWT (issued by the server's Google sign-in endpoint) via a GET query
/// parameter, stores it in an <c>AuthToken</c> cookie, builds a cookie identity
/// from the JWT claims using <see cref="JwtCookieHelper"/>, and signs in with the
/// default cookie scheme.
/// Using GET avoids cross-origin issues that arise with POST redirects from the
/// Google OAuth flow.
/// </summary>
public class GoogleCallbackModel : PageModel
{
    private readonly ILogger<GoogleCallbackModel> _logger;

    public GoogleCallbackModel(ILogger<GoogleCallbackModel> logger) => _logger = logger;

    // Accept token via GET to avoid cross-origin issues with POST
    public async Task<IActionResult> OnGetAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("[GoogleCallback] No token provided, redirecting to login");
            return RedirectToPage("/Account/Login");
        }

        try
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddHours(8)
            };
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
