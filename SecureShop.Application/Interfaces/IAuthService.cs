using SecureShop.Application.DTOs.Auth;

namespace SecureShop.Application.Interfaces;

/// <summary>
/// Defines authentication operations: registration, credential-based login,
/// and OAuth 2.0 sign-in via Google.
/// Implementations are responsible for password hashing, JWT creation,
/// and ASP.NET Core Identity integration.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Creates a new user account and returns a signed JWT.
    /// </summary>
    /// <param name="dto">Registration details (name, email, password).</param>
    /// <returns>An <see cref="AuthResponseDto"/> containing the bearer token and basic profile info.</returns>
    /// <exception cref="System.InvalidOperationException">Thrown when the email is already registered.</exception>
    Task<AuthResponseDto> RegisterAsync(RegisterDto dto);

    /// <summary>
    /// Validates credentials and returns a signed JWT on success.
    /// </summary>
    /// <param name="dto">Email and password supplied by the user.</param>
    /// <returns>An <see cref="AuthResponseDto"/> containing the bearer token.</returns>
    /// <exception cref="System.UnauthorizedAccessException">Thrown when credentials are invalid.</exception>
    Task<AuthResponseDto> LoginAsync(LoginDto dto);

    /// <summary>
    /// Exchanges a Google ID token for a local account and returns a signed JWT.
    /// Creates the account automatically if this is the user's first sign-in.
    /// </summary>
    /// <param name="dto">Contains the Google ID token from the OAuth 2.0 flow.</param>
    /// <returns>An <see cref="AuthResponseDto"/> containing the bearer token.</returns>
    Task<AuthResponseDto> GoogleSignInAsync(GoogleSignInDto dto);
}