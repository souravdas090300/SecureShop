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

public class VerifyEmailModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthService _authService;
    private readonly ILogger<VerifyEmailModel> _logger;

    [BindProperty] public string Email          { get; set; } = string.Empty;
    [BindProperty] public string PendingUserId  { get; set; } = string.Empty;
    [BindProperty] public string PendingToken   { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please enter the 6-digit code.")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "The code must be exactly 6 digits.")]
    public string OtpCode { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public VerifyEmailModel(
        UserManager<ApplicationUser> userManager,
        IAuthService authService,
        ILogger<VerifyEmailModel> logger)
    {
        _userManager = userManager;
        _authService = authService;
        _logger = logger;
    }

    public IActionResult OnGet()
    {
        Email         = TempData["PendingEmail"]?.ToString()  ?? string.Empty;
        PendingUserId = TempData["PendingUserId"]?.ToString() ?? string.Empty;
        PendingToken  = TempData["PendingToken"]?.ToString()  ?? string.Empty;

        if (string.IsNullOrEmpty(Email))
        {
            // Arrived here without context — redirect to register.
            return RedirectToPage("/Account/Register");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        if (string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(PendingUserId))
        {
            ErrorMessage = "Session expired. Please register again.";
            return Page();
        }

        var user = await _userManager.FindByIdAsync(PendingUserId);
        if (user == null)
        {
            ErrorMessage = "Account not found. Please register again.";
            return Page();
        }

        var stored = await _userManager.GetAuthenticationTokenAsync(user, "SecureShop", "EmailVerifyOTP");
        if (string.IsNullOrEmpty(stored))
        {
            ErrorMessage = "Verification code has expired. Please register again.";
            return Page();
        }

        var parts      = stored.Split('|', 2);
        var storedCode = parts[0];
        var expired    = parts.Length < 2 || !DateTime.TryParse(parts[1], null,
                             System.Globalization.DateTimeStyles.RoundtripKind, out var expiry)
                         || DateTime.UtcNow > expiry;

        if (expired)
        {
            await _userManager.RemoveAuthenticationTokenAsync(user, "SecureShop", "EmailVerifyOTP");
            ErrorMessage = "Verification code has expired. Please register again.";
            return Page();
        }

        if (!string.Equals(OtpCode.Trim(), storedCode, StringComparison.Ordinal))
        {
            ErrorMessage = "Incorrect code. Please try again.";
            return Page();
        }

        // Code is valid — confirm email and clean up token.
        await _userManager.RemoveAuthenticationTokenAsync(user, "SecureShop", "EmailVerifyOTP");
        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);

        _logger.LogInformation("Email verified for {Email}", user.Email);

        // Generate JWT and sign in.
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

        TempData["SuccessMessage"] = $"Welcome {authResult.FirstName}! Your email has been verified.";
        return Redirect("/");
    }
}
