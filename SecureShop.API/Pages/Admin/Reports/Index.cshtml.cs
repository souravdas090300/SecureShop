namespace SecureShop.API.Pages.Admin.Reports;

public class AdminReportsModel : AdminPageModel
{
    private readonly IConfiguration _configuration;
    public string ApiBaseUrl { get; set; } = string.Empty;

    public AdminReportsModel(IConfiguration configuration) => _configuration = configuration;
    public void OnGet() => ApiBaseUrl = _configuration["ApiBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
}
