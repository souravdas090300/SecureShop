using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace SecureShop.API.Pages.Admin;

/// <summary>
/// Page model for the admin profile page (<c>/admin/profile</c>).
/// Reads the authenticated administrator's identity claims from the <c>AdminCookie</c>
/// and exposes them as display properties.
/// </summary>
public class AdminProfileModel : AdminPageModel
{
    /// <summary>Administrator's email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Administrator's given (first) name.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Administrator's family (last) name.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>Populates profile properties from the AdminCookie identity claims.</summary>
    public void OnGet()
    {
        Email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        FirstName = User.FindFirstValue(ClaimTypes.GivenName) ?? string.Empty;
        LastName = User.FindFirstValue(ClaimTypes.Surname) ?? string.Empty;
    }
}
