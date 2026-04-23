using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.Application.DTOs.Products;
using System.Text.Json;

namespace SecureShop.API.Pages;

public class ProductsModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public List<ProductResponseDto> Products { get; set; } = new();
    
    [BindProperty(SupportsGet = true)]
    public string? SelectedCategory { get; set; }
    
    [BindProperty(SupportsGet = true)]
    public int CurrentPage { get; set; } = 1;
    
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }

    public ProductsModel(IHttpClientFactory httpClientFactory, IConfiguration configuration)
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
            
            var categoryParam = string.IsNullOrEmpty(SelectedCategory) ? "" : $"&category={SelectedCategory}";
            var response = await client.GetAsync($"{baseUrl}/api/products?page={CurrentPage}&pageSize=12{categoryParam}");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<PagedProductsResponse>(json, new JsonSerializerOptions 
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
