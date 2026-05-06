using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.Application.DTOs.Products;
using System.Text.Json;

namespace SecureShop.API.Pages;

/// <summary>
/// Page model for the full product catalogue page.
/// Supports optional category and search filters and server-side pagination
/// by delegating to the internal Products API.
/// </summary>
public class ProductsModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

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

    /// <summary>Injects the HTTP client factory and application configuration.</summary>
    public ProductsModel(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    /// <summary>
    /// Loads a page of products from the internal API, applying category and search filters.
    /// Falls back to an empty list on API errors.
    /// </summary>
    public async Task OnGetAsync([FromQuery(Name = "page")] int page = 1)
    {
        CurrentPage = Math.Max(1, page);
        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var categoryParam = string.IsNullOrEmpty(SelectedCategory) ? "" : $"&category={Uri.EscapeDataString(SelectedCategory)}";
            var searchParam = string.IsNullOrEmpty(Search) ? "" : $"&search={Uri.EscapeDataString(Search)}";
            var url = $"{baseUrl}/api/products?page={CurrentPage}&pageSize=12{categoryParam}{searchParam}";
            var response = await client.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<PagedProductsDto>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (result != null)
                {
                    Products = result.Items?.ToList() ?? new List<ProductResponseDto>();
                    TotalCount = result.TotalCount;
                    TotalPages = result.TotalPages;
                }
            }
        }
        catch (Exception ex)
        {
            _ = ex; // suppressed; non-fatal — products remain empty
        }
    }
}
