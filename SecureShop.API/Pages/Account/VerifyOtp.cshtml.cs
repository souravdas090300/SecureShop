using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using SecureShop.Domain.Entities;

namespace SecureShop.API.Pages.Account;

public class VerifyOtpModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<VerifyOtpModel> _logger;

    [BindProperty] public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please enter the 6-digit code.")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "The code must be exactly 6 digits.")]
    public string OtpCode { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public VerifyOtpModel(UserManager<ApplicationUser> userManager, ILogger<VerifyOtpModel> logger)
    {
        _userManager = userManager;
        _logger      = logger;
    }

    public IActionResult OnGet()
    {
        Email = TempData["PendingResetEmail"]?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(Email))
            return RedirectToPage("/Account/ForgotPassword");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        if (string.IsNullOrEmpty(Email))
        {
            ErrorMessage = "Session expired. Please start again.";
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Email);
        if (user == null)
        {
            // Avoid enumeration — just say invalid
            ErrorMessage = "Incorrect or expired code. Please try again.";
            return Page();
        }

        var stored = await _userManager.GetAuthenticationTokenAsync(user, "SecureShop", "PasswordResetOTP");
        if (string.IsNullOrEmpty(stored))
        {
            ErrorMessage = "Code has expired. Please request a new one.";
            return Page();
        }

        var parts      = stored.Split('|', 2);
        var storedCode = parts[0];
        var expired    = parts.Length < 2 || !DateTime.TryParse(parts[1], null,
                             System.Globalization.DateTimeStyles.RoundtripKind, out var expiry)
                         || DateTime.UtcNow > expiry;

        if (expired)
        {
            await _userManager.RemoveAuthenticationTokenAsync(user, "SecureShop", "PasswordResetOTP");
            ErrorMessage = "Code has expired. Please request a new one.";
            return Page();
        }

        if (!string.Equals(OtpCode.Trim(), storedCode, StringComparison.Ordinal))
        {
            ErrorMessage = "Incorrect code. Please try again.";
            return Page();
        }

        // OTP valid — generate a password reset token and forward to reset page.
        await _userManager.RemoveAuthenticationTokenAsync(user, "SecureShop", "PasswordResetOTP");
        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);

        TempData["ResetEmail"] = Email;
        TempData["ResetToken"] = resetToken;

        _logger.LogInformation("Password reset OTP verified for {Email}", Email);
        return RedirectToPage("/Account/ResetPassword");
    }
}
