using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using SecureShop.Application.DTOs.Auth;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Exceptions;

namespace SecureShop.API.Controllers;

/// <summary>
/// REST API controller for user authentication.
/// Exposes endpoints for registration, credential-based login, and Google OAuth sign-in.
/// All routes are rate-limited to protect against brute-force and credential-stuffing attacks.
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AuthController> _logger;

    /// <summary>
    /// Injects the authentication service, user manager, and structured logger.
    /// </summary>
    public AuthController(IAuthService authService, UserManager<ApplicationUser> userManager, ILogger<AuthController> logger)
    {
        _authService = authService;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Registers a new user account and returns a signed JWT bearer token.
    /// </summary>
    /// <param name="dto">Registration data: first name, last name, email, and password.</param>
    /// <returns>200 OK with <see cref="AuthResponseDto"/>, or 400/500 on failure.</returns>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterDto dto)
    {
        try
        {
            _logger.LogInformation("Registration attempt for email: {Email}", dto.Email);
            
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Invalid registration data", errors = ModelState });
            }
            
            var result = await _authService.RegisterAsync(dto);
            _logger.LogInformation("Registration successful for email: {Email}", dto.Email);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Registration failed for {Email}: {Message}", dto.Email, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during registration for {Email}", dto.Email);
            return StatusCode(500, new { message = "An error occurred during registration" });
        }
    }

    /// <summary>
    /// Authenticates a registered user with email and password.
    /// Returns a signed JWT on success, or 401 Unauthorized on invalid credentials.
    /// </summary>
    /// <param name="dto">Login credentials (email + password).</param>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto dto)
    {
        try
        {
            _logger.LogInformation("Login attempt for email: {Email}, Password length: {Length}", dto.Email, dto.Password?.Length ?? 0);
            
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Invalid login data", errors = ModelState });
            }
            
            var result = await _authService.LoginAsync(dto);
            _logger.LogInformation("Login successful for email: {Email}", dto.Email);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning("Login failed for {Email}: {Message}", dto.Email, ex.Message);
            return Unauthorized(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Login failed for {Email}: {Message}", dto.Email, ex.Message);
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during login for {Email}", dto.Email);
            return StatusCode(500, new { message = "An error occurred during login" });
        }
    }

    /// <summary>
    /// Exchanges a Google ID token for a local JWT and sets an <c>AuthToken</c> cookie.
    /// The user account is created automatically on first sign-in.
    /// </summary>
    /// <param name="dto">Contains the Google ID token from the front-end OAuth 2.0 flow.</param>
    [HttpPost("google-signin")]
    public async Task<ActionResult<AuthResponseDto>> GoogleSignIn([FromBody] GoogleSignInDto dto)
    {
        try
        {
            _logger.LogInformation("Google sign-in attempt");
            
            if (string.IsNullOrWhiteSpace(dto.IdToken))
            {
                return BadRequest(new { message = "ID token is required" });
            }
            
            var result = await _authService.GoogleSignInAsync(dto);

            // Cookie sign-in with standard ClaimTypes is handled by GoogleCallbackModel
            // after the client redirects to /Account/GoogleCallback?token=...
            // The API controller only returns the JWT; it does not write auth cookies.
            _logger.LogInformation("Google sign-in successful");
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Google sign-in failed: {Message}", ex.Message);
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Google sign-in");
            return StatusCode(500, new { message = "An error occurred during Google sign-in" });
        }
    }

    [HttpGet("users")]
    [Authorize(AuthenticationSchemes = "AdminCookie,Bearer", Roles = "Admin")]
    public IActionResult GetAllUsers()
    {
        var users = _userManager.Users
            .OrderBy(u => u.Email)
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.CreatedAt })
            .ToList();
        return Ok(users);
    }
}