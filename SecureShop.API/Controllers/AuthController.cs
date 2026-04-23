using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
        => Ok(await _authService.GoogleSignInAsync(dto));
}