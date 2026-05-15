using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecureShop.API.Pages.Admin.Orders;

/// <summary>
/// Page model for the admin orders list page.
/// Exposes the API base URL to the Razor view so the client-side script
/// can fetch and display all orders via the Orders API.
/// </summary>
public class AdminOrdersModel : AdminPageModel
{
    private readonly IConfiguration _configuration;

    /// <summary>Internal API base URL passed to the view for AJAX calls.</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Injects application configuration.</summary>
    public AdminOrdersModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>Resolves the API base URL on page load.</summary>
    public void OnGet()
    {
        ApiBaseUrl = _configuration["ApiBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
    }
}
