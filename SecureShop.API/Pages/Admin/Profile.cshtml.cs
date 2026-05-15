using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace SecureShop.API.Pages.Admin;

public class AdminProfileModel : AdminPageModel
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    public void OnGet()
    {
        Email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        FirstName = User.FindFirstValue(ClaimTypes.GivenName) ?? string.Empty;
        LastName = User.FindFirstValue(ClaimTypes.Surname) ?? string.Empty;
    }
}
