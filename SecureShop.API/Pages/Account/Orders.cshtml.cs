using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SecureShop.API.Pages.Account;

/// <summary>
/// Page model for the authenticated user's order history page.
/// Requires authentication. Calls the internal Orders API (<c>/api/orders/my</c>)
/// with the user's JWT (read from the <c>AuthToken</c> cookie) to fetch all past orders.
/// </summary>
[Authorize]
public class OrdersModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrdersModel> _logger;

    public OrdersModel(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OrdersModel> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public List<OrderDto> Orders { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            var token = Request.Cookies["AuthToken"];
            if (string.IsNullOrEmpty(token))
            {
                ErrorMessage = "You must be logged in to view orders.";
                return;
            }

            var client = _httpClientFactory.CreateClient();

            // Use Request.Scheme + Host so it works both locally and on Render
            // without needing an ApiBaseUrl config value.
            var baseUrl = $"http://localhost:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}";
            
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync($"{baseUrl}/api/orders/my");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                Orders = JsonSerializer.Deserialize<List<OrderDto>>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                }) ?? new List<OrderDto>();
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                ErrorMessage = "Session expired. Please log in again.";
            }
            else
            {
                ErrorMessage = "Unable to load orders. Please try again later.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading orders");
            ErrorMessage = "An error occurred while loading your orders.";
        }
    }
}

public class OrderDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemDto
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
