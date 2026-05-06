namespace SecureShop.Application.DTOs.Auth;

/// <summary>
/// Carries the Google ID token issued by the Google OAuth 2.0 flow.
/// The server validates this token with the Google API before creating or
/// authenticating the associated local user account.
/// </summary>
public class GoogleSignInDto
{
    /// <summary>The ID token string returned by the Google Sign-In JavaScript library.</summary>
    public string IdToken { get; set; } = string.Empty;
}
