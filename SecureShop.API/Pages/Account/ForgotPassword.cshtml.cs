using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;

namespace SecureShop.API.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly ILogger<ForgotPasswordModel> _logger;

    [BindProperty]
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address.")]
    public string Email { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
    public string? InfoMessage  { get; set; }

    public ForgotPasswordModel(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        ILogger<ForgotPasswordModel> logger)
    {
        _userManager  = userManager;
        _emailService = emailService;
        _logger       = logger;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        // Always show the same message to avoid user enumeration.
        const string safeMessage = "If that email is registered, a 6-digit reset code has been sent.";

        try
        {
            var user = await _userManager.FindByEmailAsync(Email);

            if (user == null)
            {
                InfoMessage = safeMessage;
                return Page();
            }

            var otp    = Random.Shared.Next(100_000, 999_999).ToString();
            var expiry = DateTime.UtcNow.AddMinutes(15).ToString("O");
            await _userManager.SetAuthenticationTokenAsync(
                user, "SecureShop", "PasswordResetOTP", $"{otp}|{expiry}");

            try
            {
                await _emailService.SendAsync(
                    Email,
                    "Your SecureShop password reset code",
                    BuildResetEmail(user.FirstName, otp));
            }
            catch (Exception emailEx)
            {
                _logger.LogWarning(emailEx, "Password reset email failed for {Email}", Email);
            }

            TempData["PendingResetEmail"] = Email;
            InfoMessage = safeMessage;

            // Redirect to OTP entry page.
            return RedirectToPage("/Account/VerifyOtp");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ForgotPassword failed for {Email}", Email);
            ErrorMessage = "Something went wrong. Please try again in a moment.";
            return Page();
        }
    }

    private static string BuildResetEmail(string firstName, string otp) => $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family:Arial,sans-serif;background:#f4f4f4;padding:20px;">
          <div style="max-width:480px;margin:auto;background:#fff;border-radius:10px;padding:40px;">
            <h2 style="color:#d97706;">Password Reset</h2>
            <p>Hi {System.Net.WebUtility.HtmlEncode(firstName)},</p>
            <p>Use the code below to reset your password. It expires in <strong>15 minutes</strong>.</p>
            <div style="font-size:40px;font-weight:bold;letter-spacing:12px;text-align:center;
                        background:#fef9c3;border-radius:8px;padding:20px 0;margin:24px 0;color:#92400e;">
              {otp}
            </div>
            <p style="color:#64748b;font-size:0.875rem;">If you didn't request a password reset, you can safely ignore this email.</p>
          </div>
        </body>
        </html>
        """;
}
