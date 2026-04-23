using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecureShop.API.Pages.Admin.Orders;

[Authorize(Roles = "Admin")]
public class AdminOrdersModel : PageModel
{
    private readonly IConfiguration _configuration;

    public string ApiBaseUrl { get; set; } = string.Empty;

    public AdminOrdersModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void OnGet()
    {
        ApiBaseUrl = _configuration["ApiBaseUrl"] ?? "http://localhost:8080";
    }
}
