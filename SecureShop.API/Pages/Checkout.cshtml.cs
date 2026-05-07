using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;

namespace SecureShop.API.Pages;

/// <summary>
/// Page model for the checkout page.
/// Requires authentication (<see cref="AuthorizeAttribute"/>).
/// Collects shipping and contact details plus the cart items (posted as JSON from
/// the client-side localStorage cart), then submits the order to the internal
/// Orders API with the user's JWT from the auth cookie.
/// Card payment data is handled entirely client-side via Stripe.js — raw card
/// numbers are never bound or processed by this model.
/// </summary>
[Authorize]
public class CheckoutModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CheckoutModel> _logger;

    /// <summary>Customer's given name, required for the shipping label.</summary>
    [BindProperty]
    [Required(ErrorMessage = "First name is required")]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Customer's family name, required for the shipping label.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Last name is required")]
    public string LastName { get; set; } = string.Empty;

    /// <summary>Contact email for order confirmation messages.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Optional contact phone number.</summary>
    [BindProperty]
    [Phone(ErrorMessage = "Invalid phone number")]
    public string? Phone { get; set; }

    /// <summary>Street address for delivery.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Address is required")]
    public string Address { get; set; } = string.Empty;

    /// <summary>City component of the delivery address.</summary>
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

    /// <summary>
    /// Cart items serialised as JSON by the client-side script before form submission.
    /// Expected format: <c>[{"productId":"&lt;guid&gt;","quantity":&lt;int&gt;}]</c>.
    /// </summary>
    [BindProperty]
    [Required(ErrorMessage = "Your cart is empty. Please add items before checking out.")]
    public string CartItemsJson { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public CheckoutModel(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<CheckoutModel> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
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
        // Deserialise cart items before model-state validation so we can show
        // a friendly error if the cart is empty or malformed.
        List<CartItemRequest> cartItems;
        try
        {
            cartItems = JsonSerializer.Deserialize<List<CartItemRequest>>(
                CartItemsJson ?? "[]",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<CartItemRequest>();
        }
        catch (JsonException)
        {
            cartItems = new List<CartItemRequest>();
        }

        if (cartItems.Count == 0)
        {
            ErrorMessage = "Your cart is empty. Please add items before checking out.";
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = $"http://localhost:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}";

            // Attach JWT so the Orders API can identify the user.
            var token = Request.Cookies["AuthToken"];
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            // CreateOrderDto only needs Items — card data is handled by Stripe.js client-side.
            var orderData = new
            {
                items = cartItems.Select(i => new { productId = i.ProductId, quantity = i.Quantity })
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

                if (result != null && result.Id != Guid.Empty)
                {
                    TempData["SuccessMessage"] = $"Order #{result.Id} placed successfully!";
                    TempData["OrderId"] = result.Id.ToString();
                    return RedirectToPage("/Account/Orders");
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Order creation failed ({StatusCode}): {Body}", response.StatusCode, errorContent);
                ErrorMessage = "Failed to process your order. Please try again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Checkout error for user {Email}", Email);
            ErrorMessage = "An error occurred while processing your order. Please try again.";
        }

        return Page();
    }

    /// <summary>Represents a single cart line item as posted from the client-side cart.</summary>
    private sealed class CartItemRequest
    {
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
    }

    private sealed class OrderResponse
    {
        public Guid Id { get; set; }
        public string? Status { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
