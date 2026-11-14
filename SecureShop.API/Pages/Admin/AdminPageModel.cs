using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace SecureShop.API.Pages.Admin;

/// <summary>
/// Base page model for all admin Razor Pages.
/// Enforces that the requesting user is authenticated with the AdminCookie scheme
/// and belongs to the Admin role. It also generates a fresh JWT for API calls
/// so the frontend never uses the customer's token.
/// </summary>
[Authorize(Policy = "AdminOnly")]
public class AdminPageModel : PageModel
{
    public string AdminName { get; protected set; } = string.Empty;
    public string AdminFirstName { get; protected set; } = string.Empty;
    public string AdminLastName { get; protected set; } = string.Empty;
    public string AdminEmail { get; protected set; } = string.Empty;
    public string AdminJwt { get; protected set; } = string.Empty;

    public override async Task OnPageHandlerExecutionAsync(
        PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var result = await context.HttpContext.AuthenticateAsync("AdminCookie");

        if (!result.Succeeded || result.Principal?.IsInRole("Admin") != true)
        {
            context.Result = new RedirectResult("/admin/login");
            return;
        }

        // Populate the admin identity properties used by the layout and profile page
        AdminName      = result.Principal.Identity?.Name ?? "Administrator";
        AdminFirstName = result.Principal.FindFirstValue(ClaimTypes.GivenName) ?? string.Empty;
        AdminLastName  = result.Principal.FindFirstValue(ClaimTypes.Surname) ?? string.Empty;
        AdminEmail     = result.Principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

        // Generate a FRESH JWT for the API, using the AdminCookie claims
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var jwtSecret = config["Jwt:Secret"] 
            ?? throw new InvalidOperationException("Jwt:Secret is missing.");

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(result.Principal.Claims),
            Expires = DateTime.UtcNow.AddMinutes(45),
            Issuer = config["Jwt:Issuer"],
            Audience = config["Jwt:Audience"],
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        AdminJwt = tokenHandler.WriteToken(token);

        await next();
    }
}