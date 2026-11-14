using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SecureShop.Domain.Entities;

namespace SecureShop.API.Pages.Admin;

public class AdminUsersModel : PageModel
{
    private const string AdminRole = "Admin";
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<AdminUsersModel> _logger;

    public AdminUsersModel(UserManager<ApplicationUser> users, ILogger<AdminUsersModel> logger)
    {
        _users = users;
        _logger = logger;
    }

    public List<ApplicationUser> Admins { get; set; } = new();
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public string CurrentUserId { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!User.IsInRole(AdminRole)) return Forbid();
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(string? email)
    {
        if (!User.IsInRole(AdminRole)) return Forbid();

        email = email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            ErrorMessage = "Enter the email address of a registered account.";
            return RedirectToPage();
        }

        var user = await _users.FindByEmailAsync(email);
        if (user is null)
        {
            ErrorMessage = "No account uses that email. The person must register or sign in with Google first.";
            return RedirectToPage();
        }

        if (!user.EmailConfirmed)
        {
            ErrorMessage = "That account's email address is not confirmed, so it cannot be made an administrator.";
            return RedirectToPage();
        }

        if (await _users.IsInRoleAsync(user, AdminRole))
        {
            StatusMessage = "That account is already an administrator.";
            return RedirectToPage();
        }

        var result = await _users.AddToRoleAsync(user, AdminRole);
        if (result.Succeeded)
        {
            _logger.LogWarning("[AdminUsers] {Actor} granted Admin to {Target}", _users.GetUserId(User), user.Id);
            StatusMessage = "Administrator added. They need to sign in at /admin/login.";
        }
        else
        {
            ErrorMessage = "The role could not be added. Check the server log.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(string? id)
    {
        if (!User.IsInRole(AdminRole)) return Forbid();

        var actorId = _users.GetUserId(User);
        if (string.IsNullOrEmpty(id) || id == actorId)
        {
            ErrorMessage = "You cannot remove your own administrator access.";
            return RedirectToPage();
        }

        var user = await _users.FindByIdAsync(id);
        if (user is null || !await _users.IsInRoleAsync(user, AdminRole))
        {
            ErrorMessage = "That account is not an administrator.";
            return RedirectToPage();
        }

        var admins = await _users.GetUsersInRoleAsync(AdminRole);
        if (admins.Count <= 1)
        {
            ErrorMessage = "There must always be at least one administrator.";
            return RedirectToPage();
        }

        var result = await _users.RemoveFromRoleAsync(user, AdminRole);
        if (result.Succeeded)
        {
            await _users.UpdateSecurityStampAsync(user);
            _logger.LogWarning("[AdminUsers] {Actor} removed Admin from {Target}", actorId, user.Id);
            StatusMessage = "Administrator access removed.";
        }
        else
        {
            ErrorMessage = "The role could not be removed. Check the server log.";
        }
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        CurrentUserId = _users.GetUserId(User) ?? string.Empty;
        var admins = await _users.GetUsersInRoleAsync(AdminRole);
        Admins = admins.OrderBy(a => a.Email, StringComparer.OrdinalIgnoreCase).ToList();
    }
}