using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecureShop.API.Pages.Admin;

/// <summary>
/// Page model for the admin logout flow.
/// Signs the admin out of the <c>AdminCookie</c> scheme and redirects to the admin login page.
/// </summary>
public class AdminLogoutModel : PageModel
{
    private readonly ILogger<AdminLogoutModel> _logger;

    /// <summary>Injects the logger.</summary>
    public AdminLogoutModel(ILogger<AdminLogoutModel> logger)
    {
        _logger = logger;
    }

    /// <summary>Signs the admin out of the AdminCookie scheme and redirects to admin login.</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        _logger.LogInformation("[AdminLogout] Admin logging out");
        
        await HttpContext.SignOutAsync("AdminCookie");
        
        _logger.LogInformation("[AdminLogout] Admin logged out successfully");
        
        return Redirect("/admin/login");
    }
}
