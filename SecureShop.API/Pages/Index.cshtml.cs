using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.Application.DTOs.Products;
using System.Text.Json;

namespace SecureShop.API.Pages;

public class IndexModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public List<ProductResponseDto> FeaturedProducts { get; set; } = new();

    public IndexModel(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task OnGetAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = _configuration["ApiBaseUrl"] ?? "http://localhost:8080";
            
            var response = await client.GetAsync($"{baseUrl}/api/products?pageSize=8");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<PagedProductsResponse>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                FeaturedProducts = result?.Items?.ToList() ?? new List<ProductResponseDto>();
            }
        }
        catch (Exception ex)
        {
            // Log error - products will be empty list
            Console.WriteLine($"Error loading products: {ex.Message}");
        }
    }

    private class PagedProductsResponse
    {
        public IEnumerable<ProductResponseDto> Items { get; set; } = new List<ProductResponseDto>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }
}
