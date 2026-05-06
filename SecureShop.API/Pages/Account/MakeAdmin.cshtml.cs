using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.Domain.Entities;

namespace SecureShop.API.Pages.Account;

/// <summary>
/// Utility page model for creating new admin accounts or elevating existing users to the Admin role.
/// Restricted to existing Admin users. Intended for initial bootstrapping via the Admin panel.
/// Remove or disable this page once the first admin account has been created in production.
/// </summary>
[Authorize(Roles = "Admin")]
public class MakeAdminModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<MakeAdminModel> _logger;

    public MakeAdminModel(
        UserManager<ApplicationUser> userManager,
        ILogger<MakeAdminModel> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string NewAdminEmail { get; set; } = string.Empty;

    [BindProperty]
    public string NewAdminFirstName { get; set; } = string.Empty;

    [BindProperty]
    public string NewAdminLastName { get; set; } = string.Empty;

    [BindProperty]
    public string NewAdminPassword { get; set; } = string.Empty;

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        _logger.LogInformation("[MakeAdmin] Attempting to assign Admin role to: {Email}", Email);

        var user = await _userManager.FindByEmailAsync(Email);
        
        if (user == null)
        {
            _logger.LogWarning("[MakeAdmin] User not found: {Email}", Email);
            ErrorMessage = $"User with email '{Email}' not found.";
            return Page();
        }

        // Check if already admin
        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        if (isAdmin)
        {
            _logger.LogInformation("[MakeAdmin] User {Email} is already an Admin", Email);
            SuccessMessage = $"User '{Email}' already has Admin role.";
            return Page();
        }

        // Add to Admin role
        var result = await _userManager.AddToRoleAsync(user, "Admin");
        
        if (result.Succeeded)
        {
            _logger.LogInformation("[MakeAdmin] Successfully added Admin role to: {Email}", Email);
            SuccessMessage = $"✅ Successfully assigned Admin role to '{Email}'! You can now log out and log back in to see admin pages.";
        }
        else
        {
            _logger.LogError("[MakeAdmin] Failed to add Admin role to {Email}: {Errors}", 
                Email, string.Join(", ", result.Errors.Select(e => e.Description)));
            ErrorMessage = $"Failed to assign Admin role: {string.Join(", ", result.Errors.Select(e => e.Description))}";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAdminAsync()
    {
        _logger.LogInformation("[MakeAdmin] Creating new admin user: {Email}", NewAdminEmail);

        // Check if user already exists
        var existing = await _userManager.FindByEmailAsync(NewAdminEmail);
        if (existing != null)
        {
            _logger.LogWarning("[MakeAdmin] User already exists: {Email}", NewAdminEmail);
            ErrorMessage = $"User with email '{NewAdminEmail}' already exists. Use 'Assign Admin Role' instead.";
            return Page();
        }

        // Create new user
        var newUser = new ApplicationUser
        {
            UserName = NewAdminEmail,
            Email = NewAdminEmail,
            FirstName = NewAdminFirstName,
            LastName = NewAdminLastName,
            EmailConfirmed = true
        };

        var createResult = await _userManager.CreateAsync(newUser, NewAdminPassword);
        
        if (!createResult.Succeeded)
        {
            _logger.LogError("[MakeAdmin] Failed to create user {Email}: {Errors}", 
                NewAdminEmail, string.Join(", ", createResult.Errors.Select(e => e.Description)));
            ErrorMessage = $"Failed to create user: {string.Join(", ", createResult.Errors.Select(e => e.Description))}";
            return Page();
        }

        // Add Admin role
        var roleResult = await _userManager.AddToRoleAsync(newUser, "Admin");
        
        if (roleResult.Succeeded)
        {
            _logger.LogInformation("[MakeAdmin] Successfully created admin user: {Email}", NewAdminEmail);
            SuccessMessage = $"✅ Admin user '{NewAdminEmail}' created successfully! You can now login at /admin/login";
            
            // Clear form
            NewAdminEmail = string.Empty;
            NewAdminFirstName = string.Empty;
            NewAdminLastName = string.Empty;
            NewAdminPassword = string.Empty;
        }
        else
        {
            _logger.LogError("[MakeAdmin] Failed to assign admin role to {Email}: {Errors}", 
                NewAdminEmail, string.Join(", ", roleResult.Errors.Select(e => e.Description)));
            ErrorMessage = $"User created but failed to assign Admin role: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}";
        }

        return Page();
    }
}
