namespace SecureShop.API.Pages.Admin.Products;

/// <summary>
/// Page model for the admin edit-product page.
/// Supplies the product ID, API base URL, and Cloudinary credentials to the
/// Razor view so client-side JavaScript can load the current product data,
/// allow image replacement via Cloudinary, and PATCH the updated product.
/// </summary>
public class AdminProductEditModel : AdminPageModel
{
    private readonly IConfiguration _configuration;

    /// <summary>Internal API base URL for the client-side Products API call.</summary>
    public string ApiBaseUrl    { get; set; } = string.Empty;

    /// <summary>Product GUID extracted from the route, passed to the client for the PUT request.</summary>
    public string ProductId     { get; set; } = string.Empty;

    /// <summary>Cloudinary cloud name used by the client-side upload widget.</summary>
    public string CloudName     { get; set; } = string.Empty;

    /// <summary>Cloudinary unsigned upload preset for direct browser-to-CDN uploads.</summary>
    public string UploadPreset  { get; set; } = string.Empty;

    /// <summary>Injects application configuration.</summary>
    public AdminProductEditModel(IConfiguration configuration) => _configuration = configuration;

    /// <summary>Resolves API and Cloudinary configuration values and the product ID for the view.</summary>
    public void OnGet(string id)
    {
        ApiBaseUrl   = _configuration["ApiBaseUrl"]               ?? "http://localhost:8080";
        ProductId    = id;
        CloudName    = _configuration["Cloudinary:CloudName"]     ?? "";
        UploadPreset = _configuration["Cloudinary:UploadPreset"]  ?? "";
    }
}
