using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecureShop.API.Pages.Admin;

/// <summary>
/// Base page model for all admin Razor Pages.
/// Enforces that the requesting user is authenticated with the <c>AdminCookie</c> scheme
/// and belongs to the <c>Admin</c> role. Redirects unauthenticated or unauthorised requests
/// to <c>/admin/login</c> before any handler executes.
/// All admin page models should inherit from this class instead of <see cref="PageModel"/>.
/// </summary>
[Authorize(AuthenticationSchemes = "AdminCookie", Roles = "Admin")]
public class AdminPageModel : PageModel
{
    /// <summary>The display name of the authenticated administrator, shown in the admin layout header.</summary>
    public string AdminName { get; protected set; } = string.Empty;

    /// <summary>
    /// Validates the <c>AdminCookie</c> identity before any page handler runs.
    /// Redirects to the admin login page when the user is not authenticated or lacks the Admin role.
    /// </summary>
    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        var result = context.HttpContext
            .AuthenticateAsync("AdminCookie").GetAwaiter().GetResult();

        if (!result.Succeeded || result.Principal?.IsInRole("Admin") != true)
        {
            // Guard against session expiry or cookie tampering: redirect to login.
            context.Result = new RedirectResult("/admin/login");
            return;
        }

        AdminName = result.Principal.Identity?.Name ?? "Administrator";
        base.OnPageHandlerExecuting(context);
    }
}
