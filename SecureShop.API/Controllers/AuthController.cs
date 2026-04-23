using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using SecureShop.Application.DTOs.Auth;
using SecureShop.Application.Interfaces;

namespace SecureShop.API.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;
    
    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterDto dto)
    {
        _logger.LogInformation("Registration attempt for email: {Email}", dto.Email);
        var result = await _authService.RegisterAsync(dto);
        _logger.LogInformation("Registration successful for email: {Email}", dto.Email);
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto dto)
    {
        _logger.LogInformation("Login attempt for email: {Email}, Password length: {Length}", dto.Email, dto.Password?.Length ?? 0);
        var result = await _authService.LoginAsync(dto);
        _logger.LogInformation("Login successful for email: {Email}", dto.Email);
        return Ok(result);
    }

    [HttpPost("google-signin")]
    public async Task<ActionResult<AuthResponseDto>> GoogleSignIn([FromBody] GoogleSignInDto dto)
    {
        var result = await _authService.GoogleSignInAsync(dto);

        if (!string.IsNullOrWhiteSpace(result.Token))
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = result.ExpiresAt
            };

            Response.Cookies.Append("AuthToken", result.Token, cookieOptions);
            Response.Cookies.Append("UserEmail", result.Email ?? string.Empty, cookieOptions);
            Response.Cookies.Append("UserName", result.FirstName ?? string.Empty, cookieOptions);

            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(result.Token);
            var claimsIdentity = new ClaimsIdentity(jwtToken.Claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                claimsPrincipal,
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    ExpiresUtc = result.ExpiresAt
                });
        }

        return Ok(result);
    }
}