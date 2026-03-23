namespace SecureShop.Application.DTOs.Auth;

public record RegisterDto(string FirstName, string LastName, string Email, string Password);
public record LoginDto(string Email, string Password);
public record AuthResponseDto(string Token, string Email, string FirstName, DateTime ExpiresAt);