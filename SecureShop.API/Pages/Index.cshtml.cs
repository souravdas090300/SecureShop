using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.Application.DTOs.Products;
using SecureShop.Application.Services;

namespace SecureShop.API.Pages;

/// <summary>
/// Page model for the storefront home page.
/// Fetches up to 8 featured (newest) products directly from the application
/// service layer — no HTTP round-trip required.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ProductService _productService;

    /// <summary>Featured products to display on the home page hero section.</summary>
    public List<ProductResponseDto> FeaturedProducts { get; set; } = new();

    /// <summary>Injects the product service.</summary>
    public IndexModel(ProductService productService)
    {
        _productService = productService;
    }

    /// <summary>
    /// Loads the featured product list directly from the service layer.
    /// Falls back to an empty list on any error.
    /// </summary>
    public async Task OnGetAsync()
    {
        try
        {
            var result = await _productService.GetAllAsync(null, null, 1, 8);
            FeaturedProducts = result?.Items?.ToList() ?? new List<ProductResponseDto>();
        }
        catch (Exception ex)
        {
            _ = ex; // non-fatal — home page shows empty product section
        }
    }
}
