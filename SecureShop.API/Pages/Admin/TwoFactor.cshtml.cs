using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.Domain.Entities;

namespace SecureShop.API.Pages.Admin;

/// <summary>
/// Admin self-service TOTP enrollment: setup key display, enable/disable with
/// password confirmation, and recovery-code generation. Requires the
/// AdminCookie session via <see cref="AdminPageModel"/>.
/// </summary>
public class TwoFactorModel : AdminPageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public TwoFactorModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public bool IsEnabled { get; private set; }
    public string? SetupKey { get; private set; }
    public string? AuthenticatorUri { get; private set; }
    public IEnumerable<string>? RecoveryCodes { get; private set; }
    public bool ShowRecoveryCodes => RecoveryCodes is not null;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    private async Task<ApplicationUser?> CurrentAdminAsync()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return userId is null ? null : await _userManager.FindByIdAsync(userId);
    }

    public async Task OnGetAsync()
    {
        var user = await CurrentAdminAsync();
        IsEnabled = user is not null && await _userManager.GetTwoFactorEnabledAsync(user);
    }

    /// <summary>Step 1: generate a fresh authenticator key and show it for manual entry.</summary>
    public async Task<IActionResult> OnPostSetupAsync()
    {
        var user = await CurrentAdminAsync();
        if (user is null) return Redirect("/admin/login");

        await _userManager.ResetAuthenticatorKeyAsync(user);
        SetupKey = await _userManager.GetAuthenticatorKeyAsync(user);
        AuthenticatorUri = $"otpauth://totp/SecureShop:{Uri.EscapeDataString(user.Email ?? user.UserName!)}?secret={SetupKey}&issuer=SecureShop";
        return Page();
    }

    /// <summary>Step 2: verify a TOTP code, then enable 2FA and issue recovery codes.</summary>
    public async Task<IActionResult> OnPostEnableAsync(string code)
    {
        var user = await CurrentAdminAsync();
        if (user is null) return Redirect("/admin/login");

        if (!await _userManager.VerifyTwoFactorTokenAsync(user, "Authenticator", code ?? string.Empty))
        {
            Message = "That code did not match. Check your authenticator app and try again.";
            IsError = true;
            return await ReloadSetupAsync(user);
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        RecoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        Message = "Two-factor authentication enabled. Save your recovery codes now — they are shown only once.";
        IsEnabled = true;
        return Page();
    }

    /// <summary>Disable 2FA — requires the account password.</summary>
    public async Task<IActionResult> OnPostDisableAsync(string password)
    {
        var user = await CurrentAdminAsync();
        if (user is null) return Redirect("/admin/login");

        var check = await _signInManager.CheckPasswordSignInAsync(user, password ?? string.Empty, false);
        if (!check.Succeeded)
        {
            Message = "Password confirmation failed.";
            IsError = true;
            return await ReloadAsync();
        }

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        Message = "Two-factor authentication disabled.";
        return await ReloadAsync();
    }

    /// <summary>Issue a fresh set of recovery codes (invalidates the old ones).</summary>
    public async Task<IActionResult> OnPostRecoveryCodesAsync()
    {
        var user = await CurrentAdminAsync();
        if (user is null) return Redirect("/admin/login");

        RecoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return Page();
    }

    private async Task<PageResult> ReloadSetupAsync(ApplicationUser user)
    {
        SetupKey = await _userManager.GetAuthenticatorKeyAsync(user);
        AuthenticatorUri = $"otpauth://totp/SecureShop:{Uri.EscapeDataString(user.Email ?? user.UserName!)}?secret={SetupKey}&issuer=SecureShop";
        return Page();
    }

    private async Task<PageResult> ReloadAsync()
    {
        var user = await CurrentAdminAsync();
        IsEnabled = user is not null && await _userManager.GetTwoFactorEnabledAsync(user);
        return Page();
    }
}
