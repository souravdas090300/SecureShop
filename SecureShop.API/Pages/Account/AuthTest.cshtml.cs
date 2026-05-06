using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecureShop.API.Pages.Account;

public class AuthTestModel : PageModel
{
    public bool IsAuthenticated { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string AuthenticationType { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int ClaimCount { get; set; }
    public Dictionary<string, string> Claims { get; set; } = new();

    public void OnGet()
    {
        IsAuthenticated = User.Identity?.IsAuthenticated ?? false;
        UserName = User.Identity?.Name ?? "(null)";
        AuthenticationType = User.Identity?.AuthenticationType ?? "(null)";
        
        Email = User.FindFirstValue(ClaimTypes.Email) ?? "(not found)";
        FirstName = User.FindFirstValue(ClaimTypes.GivenName) ?? "(not found)";
        LastName = User.FindFirstValue(ClaimTypes.Surname) ?? "(not found)";
        
        if (User.Claims != null)
        {
            ClaimCount = User.Claims.Count();
            Claims = User.Claims.ToDictionary(c => c.Type, c => c.Value);
        }
    }
}
