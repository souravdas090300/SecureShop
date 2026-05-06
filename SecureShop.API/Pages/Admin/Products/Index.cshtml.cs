using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecureShop.API.Pages.Admin.Products;

/// <summary>
/// Page model for the admin product list page.
/// Exposes the API base URL and any pending success message (from a
/// create/edit redirect) to the Razor view for client-side rendering.
/// </summary>
public class AdminProductsModel : AdminPageModel
{
    private readonly IConfiguration _configuration;

    /// <summary>Displays a one-time success message after a successful create or edit.</summary>
    public string? SuccessMessage { get; set; }

    /// <summary>Internal API base URL passed to the view for AJAX calls.</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Injects application configuration.</summary>
    public AdminProductsModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>Resolves the API base URL and reads any pending TempData success message.</summary>
    public void OnGet()
    {
        ApiBaseUrl = _configuration["ApiBaseUrl"] ?? "http://localhost:8080";
        SuccessMessage = TempData["SuccessMessage"] as string;
    }
}
