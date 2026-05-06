using Microsoft.AspNetCore.Authentication.Cookies;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace SecureShop.API.Helpers;

/// <summary>
/// Shared helper for converting a raw JWT string into an ASP.NET Core
/// <see cref="ClaimsPrincipal"/> that uses standard <see cref="ClaimTypes"/>
/// so that <c>User.FindFirstValue(ClaimTypes.Email)</c> and <c>User.Identity.Name</c>
/// work correctly in Razor Pages and the shared layout navbar.
/// </summary>
internal static class JwtCookieHelper
{
    /// <summary>
    /// Parses <paramref name="token"/> and builds a <see cref="ClaimsPrincipal"/>
    /// authenticated with <see cref="CookieAuthenticationDefaults.AuthenticationScheme"/>.
    /// </summary>
    /// <param name="token">A signed JWT string returned by the Auth API.</param>
    /// <param name="overrideEmail">
    ///   When provided (e.g. from the Auth API response body), takes precedence over the
    ///   claim value extracted from the JWT, which may use a vendor-specific claim name.
    /// </param>
    /// <param name="overrideFirstName">Optional first-name override (same rationale as <paramref name="overrideEmail"/>).</param>
    /// <param name="overrideLastName">Optional last-name override.</param>
    /// <returns>A <see cref="ClaimsPrincipal"/> ready to pass to <c>HttpContext.SignInAsync</c>.</returns>
    internal static ClaimsPrincipal BuildPrincipal(
        string token,
        string? overrideEmail = null,
        string? overrideFirstName = null,
        string? overrideLastName = null)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Resolve email — prefer an explicit override, fall back to common JWT claim names.
        var email = overrideEmail
            ?? jwtToken.Claims.FirstOrDefault(c =>
                c.Type == ClaimTypes.Email ||
                c.Type == "email" ||
                c.Type == JwtRegisteredClaimNames.Email)?.Value
            ?? string.Empty;

        // Resolve first name.
        var firstName = overrideFirstName
            ?? jwtToken.Claims.FirstOrDefault(c =>
                c.Type == ClaimTypes.GivenName ||
                c.Type == "given_name" ||
                c.Type == "firstName")?.Value
            ?? string.Empty;

        // Resolve last name.
        var lastName = overrideLastName
            ?? jwtToken.Claims.FirstOrDefault(c =>
                c.Type == ClaimTypes.Surname ||
                c.Type == "family_name" ||
                c.Type == "lastName")?.Value
            ?? string.Empty;

        // Resolve stable user ID from the JWT subject claim.
        var nameIdentifier = jwtToken.Claims.FirstOrDefault(c =>
            c.Type == JwtRegisteredClaimNames.Sub ||
            c.Type == ClaimTypes.NameIdentifier)?.Value
            ?? string.Empty;

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name,           firstName),  // Populates User.Identity.Name in the navbar
            new(ClaimTypes.Email,          email),
            new(ClaimTypes.GivenName,      firstName),
            new(ClaimTypes.Surname,        lastName),
            new(ClaimTypes.NameIdentifier, nameIdentifier),
        };

        // Preserve any role claims (e.g. "Customer", "Admin") from the JWT.
        var roleClaims = jwtToken.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role" || c.Type == "roles")
            .Select(c => new Claim(ClaimTypes.Role, c.Value));
        claims.AddRange(roleClaims);

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
