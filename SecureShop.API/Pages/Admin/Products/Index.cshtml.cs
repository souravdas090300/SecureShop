using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecureShop.API.Pages.Admin.Products;

[Authorize(Roles = "Admin")]
public class AdminProductsModel : PageModel
{
    private readonly IConfiguration _configuration;

    public string? SuccessMessage { get; set; }
    public string ApiBaseUrl { get; set; } = string.Empty;

    public AdminProductsModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void OnGet()
    {
        ApiBaseUrl = _configuration["ApiBaseUrl"] ?? "http://localhost:8080";
        SuccessMessage = TempData["SuccessMessage"] as string;
    }
}
