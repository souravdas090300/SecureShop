using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecureShop.API.Pages.Admin;

[Authorize(Roles = "Admin")]
public class AdminIndexModel : PageModel
{
    private readonly IConfiguration _configuration;

    public string AdminName { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;

    public AdminIndexModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void OnGet()
    {
        // Get admin name from auth cookie
        AdminName = Request.Cookies["UserName"] ?? "Administrator";
        ApiBaseUrl = _configuration["ApiBaseUrl"] ?? "http://localhost:8080";
    }
}
