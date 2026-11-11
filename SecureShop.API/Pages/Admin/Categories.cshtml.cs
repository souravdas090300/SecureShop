using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace SecureShop.API.Pages.Admin;

public class CategoriesModel : PageModel
{
    public string ApiBaseUrl { get; set; } = string.Empty;
    public CategoriesModel(IConfiguration configuration)
    {
        ApiBaseUrl = configuration["ApiBaseUrl"] ?? "http://localhost:8080";
    }
    public void OnGet() { }
}