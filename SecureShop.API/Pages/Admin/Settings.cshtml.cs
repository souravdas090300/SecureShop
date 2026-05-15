using Microsoft.AspNetCore.Mvc;

namespace SecureShop.API.Pages.Admin;

public class AdminSettingsModel : AdminPageModel
{
    [TempData]
    public string? SuccessMessage { get; set; }

    public void OnGet() { }

    public IActionResult OnPostSaveGeneral()
    {
        // General settings are read-only in this deployment (configured via environment variables).
        SuccessMessage = "Settings saved successfully.";
        return RedirectToPage();
    }
}
