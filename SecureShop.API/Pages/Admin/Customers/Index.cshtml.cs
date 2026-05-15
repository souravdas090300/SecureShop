namespace SecureShop.API.Pages.Admin.Customers;

/// <summary>
/// Page model for the admin customer list page.
/// Provides the API base URL to the view for client-side customer data loading.
/// </summary>
public class AdminCustomersModel : AdminPageModel
{
    private readonly IConfiguration _configuration;

    /// <summary>Internal API base URL passed to the view for AJAX calls.</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Injects application configuration.</summary>
    public AdminCustomersModel(IConfiguration configuration) => _configuration = configuration;

    /// <summary>Resolves the API base URL on page load.</summary>
    public void OnGet() => ApiBaseUrl = _configuration["ApiBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
}
