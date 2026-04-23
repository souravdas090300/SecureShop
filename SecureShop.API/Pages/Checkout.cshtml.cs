using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;

namespace SecureShop.API.Pages;

[Authorize]
public class CheckoutModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    [BindProperty]
    [Required(ErrorMessage = "First name is required")]
    public string FirstName { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Last name is required")]
    public string LastName { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Phone(ErrorMessage = "Invalid phone number")]
    public string? Phone { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Address is required")]
    public string Address { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "City is required")]
    public string City { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "State is required")]
    public string State { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "ZIP code is required")]
    public string ZipCode { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Country is required")]
    public string Country { get; set; } = "US";

    [BindProperty]
    [Required(ErrorMessage = "Card number is required")]
    public string CardNumber { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Expiry date is required")]
    public string ExpiryDate { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "CVV is required")]
    public string CVV { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public CheckoutModel(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public void OnGet()
    {
        // Pre-fill user information if available
        if (User.Identity?.IsAuthenticated == true)
        {
            Email = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
            FirstName = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.GivenName)?.Value ?? "";
            LastName = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Surname)?.Value ?? "";
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = _configuration["ApiBaseUrl"] ?? "http://localhost:8080";

            // Get JWT token from cookie
            var token = Request.Cookies["AuthToken"];
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            // Create order data
            var orderData = new
            {
                shippingAddress = new
                {
                    firstName = FirstName,
                    lastName = LastName,
                    email = Email,
                    phone = Phone,
                    address = Address,
                    city = City,
                    state = State,
                    zipCode = ZipCode,
                    country = Country
                },
                paymentMethod = new
                {
                    cardNumber = CardNumber.Replace(" ", ""),
                    expiryDate = ExpiryDate,
                    cvv = CVV
                }
            };

            var json = JsonSerializer.Serialize(orderData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{baseUrl}/api/orders", content);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OrderResponse>(responseJson, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (result?.Id != null)
                {
                    // Clear cart in browser (will be done via JavaScript)
                    TempData["SuccessMessage"] = $"Order #{result.Id} placed successfully!";
                    TempData["OrderId"] = result.Id;
                    return RedirectToPage("/Account/Orders");
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                ErrorMessage = "Failed to process your order. Please try again.";
                Console.WriteLine($"Order creation failed: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "An error occurred while processing your order. Please try again.";
            Console.WriteLine($"Checkout error: {ex.Message}");
        }

        return Page();
    }

    private class OrderResponse
    {
        public int Id { get; set; }
        public string? Status { get; set; }
        public decimal Total { get; set; }
    }
}
