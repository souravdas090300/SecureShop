using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecureShop.API.Pages;

/// <summary>
/// Page model for the shopping cart page.
/// Cart state is managed entirely client-side (localStorage); this model only
/// initialises the page and logs the authentication status for diagnostics.
/// </summary>
public class CartModel : PageModel
{
    private readonly ILogger<CartModel> _logger;

    /// <summary>Injects the logger.</summary>
    public CartModel(ILogger<CartModel> logger)
    {
        _logger = logger;
    }

    /// <summary>Logs the current authentication state on GET.</summary>
    public void OnGet()
    {
        _logger.LogInformation("[Cart] Page loaded for user: {IsAuthenticated}", 
            User.Identity?.IsAuthenticated ?? false);
    }
}
