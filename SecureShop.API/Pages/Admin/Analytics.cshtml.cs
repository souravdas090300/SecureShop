using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace SecureShop.API.Pages.Admin;

public class AnalyticsModel : AdminPageModel
{
    private readonly IConfiguration _configuration;
    public string ApiBaseUrl { get; set; } = string.Empty;

    public AnalyticsModel(IConfiguration configuration) 
    {
        _configuration = configuration;
    }

    public void OnGet()
    {
        ApiBaseUrl = _configuration["ApiBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
    }
}