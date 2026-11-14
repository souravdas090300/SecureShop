using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace SecureShop.API.Pages.Admin;

public class CategoriesModel : AdminPageModel
{
    private readonly IConfiguration _configuration;
    
    public string ApiBaseUrl { get; set; } = string.Empty;

    public CategoriesModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void OnGet()
    {
        ApiBaseUrl = _configuration["ApiBaseUrl"] ?? "http://localhost:8080";
    }
}