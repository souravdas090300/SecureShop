using Microsoft.AspNetCore.Mvc;

namespace SecureShop.API.Pages.Admin;

/// <summary>
/// Page model for the admin settings page (<c>/admin/settings</c>).
/// Displays store configuration across four tabs: General, Security, Notifications, and System.
/// Most settings are read-only (managed via Railway environment variables);
/// only the General tab has an active Save handler as an example.
/// </summary>
public class AdminSettingsModel : AdminPageModel
{
    /// <summary>Success message shown in the top alert after a successful save operation.</summary>
    [TempData]
    public string? SuccessMessage { get; set; }

    /// <summary>The public base URL of the store, resolved from the current HTTP request.</summary>
    public string StoreUrl { get; set; } = string.Empty;

    /// <summary>Resolves and exposes the store URL from the incoming request context.</summary>
    public void OnGet()
    {
        StoreUrl = $"{Request.Scheme}://{Request.Host}";
    }

    /// <summary>
    /// Handles the General settings form POST.
    /// In this deployment settings are managed via environment variables,
    /// so this handler simply acknowledges the save and redirects.
    /// </summary>
    public IActionResult OnPostSaveGeneral()
    {
        // General settings are read-only in this deployment (configured via environment variables).
        SuccessMessage = "Settings saved successfully.";
        return RedirectToPage();
    }
}
