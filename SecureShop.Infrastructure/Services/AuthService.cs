using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
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
    private readonly IConfiguration _config;

    public AuthService(UserManager<ApplicationUser> userManager, IConfiguration config)
    {
        _userManager = userManager;
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

        await _userManager.AddToRoleAsync(user, "Customer");
        return GenerateToken(user);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email)
            ?? throw new DomainException("Invalid credentials");

        if (!await _userManager.CheckPasswordAsync(user, dto.Password))
            throw new DomainException("Invalid credentials");

        return GenerateToken(user);
    }

    private AuthResponseDto GenerateToken(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));
        var expires = DateTime.UtcNow.AddHours(8);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim(ClaimTypes.GivenName, user.FirstName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"], audience: _config["Jwt:Audience"],
            claims: claims, expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new AuthResponseDto(
            new JwtSecurityTokenHandler().WriteToken(token),
            user.Email!, user.FirstName, expires);
    }
}