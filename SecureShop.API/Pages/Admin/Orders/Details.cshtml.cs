namespace SecureShop.API.Pages.Admin.Orders;

/// <summary>
/// Page model for the admin order details page.
/// Passes the order ID and API base URL to the view so client-side JavaScript
/// can load and display the full order, and allows status updates via the Orders API.
/// </summary>
public class AdminOrderDetailsModel : AdminPageModel
{
    private readonly IConfiguration _configuration;

    /// <summary>Internal API base URL passed to the view for AJAX calls.</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>The order ID extracted from the route and passed to the view.</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Injects application configuration.</summary>
    public AdminOrderDetailsModel(IConfiguration configuration) => _configuration = configuration;

    /// <summary>Populates API base URL and order ID for the view.</summary>
    public void OnGet(string id)
    {
        ApiBaseUrl = _configuration["ApiBaseUrl"] ?? "http://localhost:8080";
        OrderId = id;
    }
}
