using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using SecureShop.Domain.Entities;
using SecureShop.Application.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace SecureShop.API.Pages.Account;

public class VerifyEmailModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly ILogger<VerifyEmailModel> _logger;

    public VerifyEmailModel(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        ILogger<VerifyEmailModel> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _logger = logger;
    }

    [BindProperty]
    [Required(ErrorMessage = "Please enter the 6-digit code")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be exactly 6 digits")]
    public string OtpCode { get; set; } = string.Empty;

    public string? Email { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public IActionResult OnGet()
    {
        // Retrieve the pending email from TempData
        Email = TempData["PendingEmail"]?.ToString();
        SuccessMessage = TempData["SuccessMessage"]?.ToString();

        if (string.IsNullOrEmpty(Email))
        {
            // If no email in TempData, they shouldn't be here.
            return RedirectToPage("/Account/Register");
        }

        // Keep it in TempData for the next request
        TempData.Keep("PendingEmail");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Email = TempData["PendingEmail"]?.ToString();
        if (string.IsNullOrEmpty(Email))
        {
            return RedirectToPage("/Account/Register");
        }

        if (!ModelState.IsValid)
        {
            TempData.Keep("PendingEmail");
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Email);
        if (user == null)
        {
            ErrorMessage = "User not found.";
            TempData.Keep("PendingEmail");
            return Page();
        }

        // 1. Retrieve the stored OTP
        var storedToken = await _userManager.GetAuthenticationTokenAsync(user, "SecureShop", "EmailVerifyOTP");
        if (string.IsNullOrEmpty(storedToken))
        {
            ErrorMessage = "No verification code found. Please register again.";
            TempData.Keep("PendingEmail");
            return Page();
        }

        var parts = storedToken.Split('|');
        if (parts.Length != 2)
        {
            ErrorMessage = "Invalid verification state. Please register again.";
            TempData.Keep("PendingEmail");
            return Page();
        }

        var validOtp = parts[0];
        var expiryStr = parts[1];

        // 2. Check Expiry
        if (DateTime.TryParse(expiryStr, out var expiry) && DateTime.UtcNow > expiry)
        {
            ErrorMessage = "The verification code has expired. Please click 'Resend Code'.";
            TempData.Keep("PendingEmail");
            return Page();
        }

        // 3. Verify the Code
        if (OtpCode != validOtp)
        {
            ErrorMessage = "Invalid verification code. Please try again.";
            TempData.Keep("PendingEmail");
            return Page();
        }

        // 4. Success! Confirm the email
        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);
        
        // Clean up the token
        await _userManager.RemoveAuthenticationTokenAsync(user, "SecureShop", "EmailVerifyOTP");

        TempData["SuccessMessage"] = "Email verified successfully! You can now log in.";
        return RedirectToPage("/Account/Login");
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        Email = TempData["PendingEmail"]?.ToString();
        if (string.IsNullOrEmpty(Email))
        {
            return RedirectToPage("/Account/Register");
        }

        var user = await _userManager.FindByEmailAsync(Email);
        if (user == null)
        {
            return RedirectToPage("/Account/Register");
        }

        // Generate a new OTP
        var otp = Random.Shared.Next(100_000, 999_999).ToString();
        var expiry = DateTime.UtcNow.AddMinutes(15).ToString("O");
        
        await _userManager.SetAuthenticationTokenAsync(
            user, "SecureShop", "EmailVerifyOTP", $"{otp}|{expiry}");

        try
        {
            await _emailService.SendAsync(
                Email,
                "Your SecureShop verification code",
                BuildOtpEmail(user.FirstName, otp));
            
            SuccessMessage = $"A new 6-digit code has been sent to {Email}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resend OTP email to {Email}", Email);
            ErrorMessage = "Failed to send email. Please try again later.";
        }

        TempData.Keep("PendingEmail");
        return Page();
    }

    private static string BuildOtpEmail(string firstName, string otp) => $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family:Arial,sans-serif;background:#f4f4f4;padding:20px;">
          <div style="max-width:480px;margin:auto;background:#fff;border-radius:10px;padding:40px;">
            <h2 style="color:#2563eb;">Verify your email</h2>
            <p>Hi {System.Net.WebUtility.HtmlEncode(firstName)},</p>
            <p>Use the code below to complete your SecureShop sign-up. It expires in <strong>15 minutes</strong>.</p>
            <div style="font-size:40px;font-weight:bold;letter-spacing:12px;text-align:center;
                        background:#f1f5f9;border-radius:8px;padding:20px 0;margin:24px 0;color:#1e293b;">
              {otp}
            </div>
            <p style="color:#64748b;font-size:0.875rem;">If you didn't create an account, you can ignore this email.</p>
          </div>
        </body>
        </html>
        """;
}