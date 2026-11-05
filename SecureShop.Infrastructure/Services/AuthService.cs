using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SecureShop.Application.DTOs.Auth;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Exceptions;

namespace SecureShop.Infrastructure.Services;

/// <summary>
/// Infrastructure-layer implementation of <see cref="IAuthService"/>.
/// Handles user registration, credential-based login, and Google OAuth sign-in
/// using ASP.NET Core Identity and JWT bearer tokens (HMAC-SHA256, 8-hour expiry).
/// </summary>
public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;

    /// <summary>Injects Identity managers, application configuration, and a structured logger.</summary>
    public AuthService(
        UserManager<ApplicationUser> userManager, 
        RoleManager<IdentityRole> roleManager,
        IConfiguration config,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
    {
        var existing = await _userManager.FindByEmailAsync(dto.Email);
        if (existing is not null) throw new DomainException("Email already registered");

        var user = new ApplicationUser
        {
            UserName = dto.Email, Email = dto.Email,
            FirstName = dto.FirstName, LastName = dto.LastName,
            LockoutEnabled = true
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            throw new DomainException(string.Join(", ", result.Errors.Select(e => e.Description)));

        // Ensure Customer role exists before adding user to it
        if (!await _roleManager.RoleExistsAsync("Customer"))
        {
            await _roleManager.CreateAsync(new IdentityRole("Customer"));
        }
        
        await _userManager.AddToRoleAsync(user, "Customer");
        return await GenerateTokenAsync(user);
    }

    /// <inheritdoc />
    public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);

        if (user == null)
        {
            _logger.LogWarning("Login failed because the account was not found");
            throw new DomainException("Invalid credentials");
        }

        if (string.IsNullOrEmpty(dto.Password))
        {
            throw new DomainException("Invalid credentials");
        }

        if (!await _userManager.GetLockoutEnabledAsync(user))
        {
            var enableLockout = await _userManager.SetLockoutEnabledAsync(user, true);
            EnsureIdentityOperationSucceeded(enableLockout, "Enabling account lockout");
        }

        if (await _userManager.IsLockedOutAsync(user))
            throw new DomainException("Invalid credentials");

        var passwordValid = await _userManager.CheckPasswordAsync(user, dto.Password!);

        if (!passwordValid)
        {
            var accessFailed = await _userManager.AccessFailedAsync(user);
            EnsureIdentityOperationSucceeded(accessFailed, "Recording failed login");
            _logger.LogWarning("Login failed because the password was invalid");
            throw new DomainException("Invalid credentials");
        }

        var resetAccessFailedCount = await _userManager.ResetAccessFailedCountAsync(user);
        EnsureIdentityOperationSucceeded(resetAccessFailedCount, "Resetting failed login count");
        _logger.LogInformation("Login successful for user {UserId}", user.Id);
        return await GenerateTokenAsync(user);
    }

    /// <inheritdoc />
    public async Task<AuthResponseDto> GoogleSignInAsync(GoogleSignInDto dto)
    {
        try
        {
            var googleClientId = _config["GoogleAuth:ClientId"];
            if (string.IsNullOrWhiteSpace(googleClientId) || googleClientId.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainException("Google Sign-In is not configured on the server.");
            }

            // Validate the Google ID token
            var settings = new GoogleJsonWebSignature.ValidationSettings()
            {
                Audience = new[] { googleClientId }
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, settings);
            if (!payload.EmailVerified)
                throw new DomainException("Google account email is not verified.");

            // Check if user exists
            var user = await _userManager.FindByEmailAsync(payload.Email);

            if (user == null)
            {
                // Create new user from Google account
                user = new ApplicationUser
                {
                    UserName = payload.Email,
                    Email = payload.Email,
                    FirstName = payload.GivenName ?? "User",
                    LastName = payload.FamilyName ?? "",
                    EmailConfirmed = payload.EmailVerified,
                    LockoutEnabled = true
                };

                var result = await _userManager.CreateAsync(user);
                if (!result.Succeeded)
                    throw new DomainException(string.Join(", ", result.Errors.Select(e => e.Description)));

                // Ensure Customer role exists before adding user to it
                if (!await _roleManager.RoleExistsAsync("Customer"))
                {
                    await _roleManager.CreateAsync(new IdentityRole("Customer"));
                }

                await _userManager.AddToRoleAsync(user, "Customer");
            }

            return await GenerateTokenAsync(user);
        }
        catch (InvalidJwtException)
        {
            throw new DomainException("Invalid Google token");
        }
    }

    /// <inheritdoc />
    public async Task<AuthResponseDto> GenerateTokenForUserAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");
        return await GenerateTokenAsync(user);
    }

    /// <summary>
    /// Builds and signs a JWT for the given user, embedding standard identity claims
    /// (sub, email, given_name, family_name, jti) plus all role claims.
    /// Token is valid for 8 hours.
    /// </summary>
    private async Task<AuthResponseDto> GenerateTokenAsync(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));
        var expires = DateTime.UtcNow.AddHours(8);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim(ClaimTypes.GivenName, user.FirstName),
            new Claim(ClaimTypes.Surname, user.LastName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var roles = await _userManager.GetRolesAsync(user);
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"], audience: _config["Jwt:Audience"],
            claims: claims, expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new AuthResponseDto(
            new JwtSecurityTokenHandler().WriteToken(token),
            user.Email!, user.FirstName, user.LastName, expires);
    }

    private static void EnsureIdentityOperationSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"{operation} failed: {string.Join(", ", result.Errors.Select(error => error.Code))}");
    }
}