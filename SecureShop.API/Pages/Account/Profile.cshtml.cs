using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.Domain.Entities;
using System.Security.Claims;

namespace SecureShop.API.Pages.Account;

/// <summary>
/// Page model for the authenticated user's profile page.
/// Requires cookie authentication. Loads the current user's details from
/// ASP.NET Core Identity and allows updating the display name.
/// </summary>
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class ProfileModel : PageModel
{
    private readonly ILogger<ProfileModel> _logger;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProfileModel(
        ILogger<ProfileModel> logger,
        UserManager<ApplicationUser> userManager)
    {
        _logger = logger;
        _userManager = userManager;
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
        
        _logger.LogInformation("[Profile] User viewing profile - Email: {Email}, FirstName: {FirstName}, LastName: {LastName}", 
            Email, FirstName, LastName);
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

    public async Task<IActionResult> OnPostDeleteAccountAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userEmail = User.FindFirstValue(ClaimTypes.Email);
        
        _logger.LogWarning("[Profile] Account deletion requested by user: {Email} (ID: {UserId})", userEmail, userId);

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogError("[Profile] Cannot delete account - User ID not found in claims");
            ErrorMessage = "Unable to delete account. Please try logging in again.";
            return Page();
        }

        try
        {
            var user = await _userManager.FindByIdAsync(userId);
            
            if (user == null)
            {
                _logger.LogError("[Profile] Cannot delete account - User not found: {UserId}", userId);
                ErrorMessage = "Account not found. Please contact support.";
                return Page();
            }

            // Delete the user
            var result = await _userManager.DeleteAsync(user);
            
            if (result.Succeeded)
            {
                _logger.LogInformation("[Profile] Account successfully deleted: {Email} (ID: {UserId})", userEmail, userId);
                
                // Sign out the user
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                
                // Redirect to home with success message
                TempData["SuccessMessage"] = "Your account has been permanently deleted. We're sorry to see you go!";
                return Redirect("/");
            }
            else
            {
                _logger.LogError("[Profile] Failed to delete account: {Errors}", 
                    string.Join(", ", result.Errors.Select(e => e.Description)));
                ErrorMessage = $"Failed to delete account: {string.Join(", ", result.Errors.Select(e => e.Description))}";
                return Page();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Profile] Error deleting account for user: {Email}", userEmail);
            ErrorMessage = "An error occurred while deleting your account. Please try again or contact support.";
            return Page();
        }
    }
}
