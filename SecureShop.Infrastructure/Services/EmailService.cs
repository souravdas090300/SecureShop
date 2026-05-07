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

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

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

        using var smtp = new SmtpClient(host, port)
        {
            EnableSsl   = true,
            Credentials = new NetworkCredential(user, password)
        };

        await smtp.SendMailAsync(message);
        _logger.LogInformation("Email sent → {To}: {Subject}", toEmail, subject);
    }
}
