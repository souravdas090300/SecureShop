using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecureShop.API.Pages.Admin;

/// <summary>
/// Page model for the admin dashboard home page.
/// Inherits authentication and role checks from <see cref="AdminPageModel"/>.
/// Provides the API base URL to the Razor view for client-side AJAX calls.
/// </summary>
public class AdminIndexModel : AdminPageModel
{
    private readonly IConfiguration _configuration;

    /// <summary>Internal API base URL, passed to the Razor view for fetch() calls.</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Injects application configuration.</summary>
    public AdminIndexModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>Resolves the API base URL on page load; admin name is set by the base class.</summary>
    public void OnGet()
    {
        // AdminName is set by base class OnPageHandlerExecuting
        ApiBaseUrl = _configuration["ApiBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
    }
}
