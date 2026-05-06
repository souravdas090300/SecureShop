using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecureShop.API.Pages;

/// <summary>
/// Page model for the About page.
/// Renders static company/team information; no data loading required.
/// </summary>
public class AboutModel : PageModel
{
    private readonly ILogger<AboutModel> _logger;

    /// <summary>Injects the logger.</summary>
    public AboutModel(ILogger<AboutModel> logger)
    {
        _logger = logger;
    }

    /// <summary>Logs a page-view event on GET.</summary>
    public void OnGet()
    {
        _logger.LogInformation("[About] Page loaded");
    }
}
