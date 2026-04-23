using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace SecureShop.API.Pages.Account;

[Authorize]
public class ProfileModel : PageModel
{
    private readonly ILogger<ProfileModel> _logger;

    public ProfileModel(ILogger<ProfileModel> logger)
    {
        _logger = logger;
    }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string FirstName { get; set; } = string.Empty;

    [BindProperty]
    public string LastName { get; set; } = string.Empty;

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
        // Load user info from claims
        Email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        FirstName = User.FindFirstValue(ClaimTypes.GivenName) ?? string.Empty;
        LastName = User.FindFirstValue(ClaimTypes.Surname) ?? string.Empty;
    }

    public IActionResult OnPost()
    {
        // Note: Profile editing would require backend API changes
        // For now, this is a read-only view
        ErrorMessage = "Profile editing is not yet implemented. Please contact support to update your information.";
        
        // Reload current data
        Email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        FirstName = User.FindFirstValue(ClaimTypes.GivenName) ?? string.Empty;
        LastName = User.FindFirstValue(ClaimTypes.Surname) ?? string.Empty;
        
        return Page();
    }
}
