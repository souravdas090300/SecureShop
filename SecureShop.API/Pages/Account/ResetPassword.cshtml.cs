using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using SecureShop.API.Helpers;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;

namespace SecureShop.API.Pages.Account;

public class ResetPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthService _authService;
    private readonly ILogger<ResetPasswordModel> _logger;

    [BindProperty] public string Email      { get; set; } = string.Empty;
    [BindProperty] public string ResetToken { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
        ErrorMessage = "Must contain uppercase, lowercase, number, and special character.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please confirm your password.")]
    [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public ResetPasswordModel(
        UserManager<ApplicationUser> userManager,
        IAuthService authService,
        ILogger<ResetPasswordModel> logger)
    {
        _userManager = userManager;
        _authService = authService;
        _logger      = logger;
    }

    public IActionResult OnGet()
    {
        Email      = TempData["ResetEmail"]?.ToString() ?? string.Empty;
        ResetToken = TempData["ResetToken"]?.ToString() ?? string.Empty;

        if (string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(ResetToken))
            return RedirectToPage("/Account/ForgotPassword");

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        if (string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(ResetToken))
        {
            ErrorMessage = "Session expired. Please request a new reset code.";
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Email);
        if (user == null)
        {
            ErrorMessage = "Password reset failed. Please try again.";
            return Page();
        }

        var result = await _userManager.ResetPasswordAsync(user, ResetToken, NewPassword);
        if (!result.Succeeded)
        {
            ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            return Page();
        }

        _logger.LogInformation("Password reset successful for {Email}", Email);

        // Auto-login after reset.
        var authResult = await _authService.GenerateTokenForUserAsync(user.Id);

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure   = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires  = DateTimeOffset.UtcNow.AddHours(8)
        };
        Response.Cookies.Append("AuthToken", authResult.Token, cookieOptions);

        var principal = JwtCookieHelper.BuildPrincipal(
            authResult.Token,
            overrideEmail:     authResult.Email,
            overrideFirstName: authResult.FirstName,
            overrideLastName:  authResult.LastName);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc   = DateTimeOffset.UtcNow.AddHours(8),
                AllowRefresh = true
            });

        TempData["SuccessMessage"] = "Your password has been reset successfully. Welcome back!";
        return Redirect("/");
    }
}
