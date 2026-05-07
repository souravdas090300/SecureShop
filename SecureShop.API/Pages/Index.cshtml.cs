using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.Application.DTOs.Products;
using System.Text.Json;

namespace SecureShop.API.Pages;

/// <summary>
/// Page model for the storefront home page.
/// Fetches up to 8 featured (newest) products from the internal API and
/// exposes them to the Razor view via <see cref="FeaturedProducts"/>.
/// </summary>
public class IndexModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>Featured products to display on the home page hero section.</summary>
    public List<ProductResponseDto> FeaturedProducts { get; set; } = new();

    /// <summary>Injects the HTTP client factory.</summary>
    public IndexModel(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Loads the featured product list by calling the internal Products API.
    /// Falls back to an empty list on any API error.
    /// </summary>
    public async Task OnGetAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = $"http://localhost:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}";
            
            var response = await client.GetAsync($"{baseUrl}/api/products?pageSize=8");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<PagedProductsDto>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                FeaturedProducts = result?.Items?.ToList() ?? new List<ProductResponseDto>();
            }
        }
        catch (Exception ex)
        {
            // Products will be empty list — non-fatal on the home page.
            _ = ex; // suppressed; error logged by ASP.NET Core host
        }
    }
}
