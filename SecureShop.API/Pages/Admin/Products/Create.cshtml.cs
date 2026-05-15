using Microsoft.AspNetCore.Mvc;

namespace SecureShop.API.Pages.Admin.Products;

/// <summary>
/// Page model for the admin create-product page.
/// Supplies the API base URL and Cloudinary credentials to the Razor view
/// so client-side JavaScript can upload product images and POST the new product.
/// </summary>
public class AdminProductCreateModel : AdminPageModel
{
    private readonly IConfiguration _configuration;

    /// <summary>Internal API base URL for the client-side Products API call.</summary>
    public string ApiBaseUrl    { get; set; } = string.Empty;

    /// <summary>Cloudinary cloud name used by the client-side upload widget.</summary>
    public string CloudName     { get; set; } = string.Empty;

    /// <summary>Cloudinary unsigned upload preset for direct browser-to-CDN uploads.</summary>
    public string UploadPreset  { get; set; } = string.Empty;

    /// <summary>Injects application configuration.</summary>
    public AdminProductCreateModel(IConfiguration configuration) => _configuration = configuration;

    /// <summary>Resolves API and Cloudinary configuration values for the view.</summary>
    public void OnGet()
    {
        ApiBaseUrl   = _configuration["ApiBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        CloudName    = _configuration["Cloudinary:CloudName"]     ?? "";
        UploadPreset = _configuration["Cloudinary:UploadPreset"]  ?? "";
    }
}
