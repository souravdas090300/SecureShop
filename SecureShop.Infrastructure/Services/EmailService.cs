using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SecureShop.Application.Interfaces;

namespace SecureShop.Infrastructure.Services;

/// <summary>
/// SMTP email sender. Configure via Email:* settings (or Railway env vars
/// Email__SmtpHost, Email__SmtpPort, Email__SmtpUser, Email__SmtpPassword, Email__FromName).
/// </summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    /// <summary>Injects application configuration and a structured logger.</summary>
    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Sends an HTML email via SMTP.
    /// Uses implicit SSL (port 465) or STARTTLS (port 587) depending on the configured port.
    /// If SMTP credentials are not configured, logs a warning and returns without throwing.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="subject">Email subject line.</param>
    /// <param name="htmlBody">Full HTML email body.</param>
    public async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        var host     = _config["Email:SmtpHost"] ?? "smtp.gmail.com";
        var port     = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
        var user     = _config["Email:SmtpUser"];
        var password = _config["Email:SmtpPassword"];
        var fromName = _config["Email:FromName"] ?? "SecureShop";

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Email not sent to {To} — SMTP credentials not configured (Email:SmtpUser / Email:SmtpPassword).", toEmail);
            return;
        }

        using var message = new MailMessage();
        message.From    = new MailAddress(user, fromName);
        message.Subject = subject;
        message.Body    = htmlBody;
        message.IsBodyHtml = true;
        message.To.Add(toEmail);

        // Port 465 uses implicit SSL; port 587 uses STARTTLS.
        // Railway blocks outbound 587 on free tier — prefer 465.
        using var smtp = new SmtpClient(host, port)
        {
            EnableSsl   = port != 587,
            Credentials = new NetworkCredential(user, password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 10000
        };

        await smtp.SendMailAsync(message);
        _logger.LogInformation("Email sent → {To}: {Subject}", toEmail, subject);
    }
}
