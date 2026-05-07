using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.Application.DTOs.Products;
using SecureShop.Application.Services;

namespace SecureShop.API.Pages;

/// <summary>
/// Page model for the full product catalogue page.
/// Supports optional category and search filters and server-side pagination
/// by calling the application service layer directly — no HTTP round-trip.
/// </summary>
public class ProductsModel : PageModel
{
    private readonly ProductService _productService;

    /// <summary>Products to display on the current page.</summary>
    public List<ProductResponseDto> Products { get; set; } = new();

    /// <summary>Bound from the query string; filters by product category.</summary>
    [BindProperty(SupportsGet = true)]
    public string? SelectedCategory { get; set; }

    /// <summary>Bound from the query string; free-text search term.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>The currently displayed page number (1-based).</summary>
    public int CurrentPage { get; set; } = 1;

    /// <summary>Total number of pages for the current filter combination.</summary>
    public int TotalPages { get; set; }

    /// <summary>Total number of matching products across all pages.</summary>
    public int TotalCount { get; set; }

    /// <summary>Injects the product service.</summary>
    public ProductsModel(ProductService productService)
    {
        _productService = productService;
    }

    /// <summary>
    /// Loads a page of products directly from the service layer, applying category and search filters.
    /// Falls back to an empty list on any error.
    /// </summary>
    public async Task OnGetAsync([FromQuery(Name = "page")] int page = 1)
    {
        CurrentPage = Math.Max(1, page);
        try
        {
            var result = await _productService.GetAllAsync(SelectedCategory, Search, CurrentPage, 12);
            if (result != null)
            {
                Products = result.Items?.ToList() ?? new List<ProductResponseDto>();
                TotalCount = result.TotalCount;
                TotalPages = result.TotalPages;
            }
        }
        catch (Exception ex)
        {
            _ = ex; // suppressed; non-fatal — products remain empty
        }
    }
}
