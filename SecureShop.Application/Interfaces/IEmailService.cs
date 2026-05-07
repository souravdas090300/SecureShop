namespace SecureShop.Application.Interfaces;

/// <summary>
/// Abstracts sending transactional emails (OTP codes, notifications).
/// </summary>
public interface IEmailService
{
    /// <summary>Sends an HTML email.</summary>
    Task SendAsync(string toEmail, string subject, string htmlBody);
}
