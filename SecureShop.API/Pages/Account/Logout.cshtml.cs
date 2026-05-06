using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecureShop.API.Pages.Account;

/// <summary>
/// Page model for the logout flow.
/// Signs the user out of the cookie authentication scheme, removes raw token cookies,
/// and redirects to the home page. Supports both GET and POST to be CSRF-safe
/// when invoked via a form submission.
/// </summary>
public class LogoutModel : PageModel
{
    /// <summary>
    /// Signs out the current user and clears all auth cookies, then redirects home.
    /// </summary>
    public async Task<IActionResult> OnGetAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        
        // Clear the raw token cookies as well
        Response.Cookies.Delete("AuthToken");
        Response.Cookies.Delete("UserEmail");
        Response.Cookies.Delete("UserName");
        
        return RedirectToPage("/Index");
    }

    /// <summary>Support POST-based logout (CSRF-safe form submission).</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        return await OnGetAsync();
    }
}
