using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SecureShop.Application.DTOs.Auth;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Exceptions;

namespace SecureShop.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IConfiguration _config;

    public AuthService(
        UserManager<ApplicationUser> userManager, 
        RoleManager<IdentityRole> roleManager,
        IConfiguration config)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _config = config;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
    {
        var existing = await _userManager.FindByEmailAsync(dto.Email);
        if (existing is not null) throw new DomainException("Email already registered");

        var user = new ApplicationUser
        {
            UserName = dto.Email, Email = dto.Email,
            FirstName = dto.FirstName, LastName = dto.LastName
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

    public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
    {
        Console.WriteLine($"[AuthService] Login attempt - Email: '{dto.Email}', Password length: {dto.Password?.Length ?? 0}");
        
        var user = await _userManager.FindByEmailAsync(dto.Email);
        
        if (user == null)
        {
            Console.WriteLine($"[AuthService] User not found for email: '{dto.Email}'");
            throw new DomainException("Invalid credentials");
        }
        
        Console.WriteLine($"[AuthService] User found: ID={user.Id}, Email={user.Email}");
        
        var passwordValid = await _userManager.CheckPasswordAsync(user, dto.Password);
        Console.WriteLine($"[AuthService] Password check result: {passwordValid}");
        
        if (!passwordValid)
        {
            Console.WriteLine($"[AuthService] Invalid password for user: '{dto.Email}'");
            throw new DomainException("Invalid credentials");
        }

        Console.WriteLine($"[AuthService] Login successful for: '{dto.Email}'");
        return await GenerateTokenAsync(user);
    }

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
                    EmailConfirmed = true // Google emails are already verified
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

    private async Task<AuthResponseDto> GenerateTokenAsync(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));
        var expires = DateTime.UtcNow.AddHours(8);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim(ClaimTypes.GivenName, user.FirstName),
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
            user.Email!, user.FirstName, expires);
    }
}