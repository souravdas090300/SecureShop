namespace SecureShop.Application.DTOs.Auth;

/// <summary>Data submitted when creating a new user account.</summary>
public record RegisterDto(string FirstName, string LastName, string Email, string Password);

/// <summary>Credentials supplied when authenticating an existing account.</summary>
public record LoginDto(string Email, string Password);

/// <summary>
/// JWT response returned after successful registration, login, or OAuth sign-in.
/// <c>Token</c> is an HMAC-SHA256-signed JWT valid until <c>ExpiresAt</c>.
/// </summary>
public record AuthResponseDto(string Token, string Email, string FirstName, string LastName, DateTime ExpiresAt);