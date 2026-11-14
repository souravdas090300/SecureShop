using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SecureShop.Application.Interfaces;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace SecureShop.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public EmailService(IConfiguration config, ILogger<EmailService> logger, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        var resendApiKey = _config["Email:ResendApiKey"];
        var fromEmail = _config["Email:FromEmail"] ?? "onboarding@resend.dev";

        // If a Resend API key is configured, use the HTTP API (works on Railway)
        if (!string.IsNullOrWhiteSpace(resendApiKey))
        {
            await SendViaResendAsync(resendApiKey, fromEmail, toEmail, subject, htmlBody);
            return;
        }

        // Fallback to SMTP (works locally, often blocked on cloud providers)
        await SendViaSmtpAsync(toEmail, subject, htmlBody);
    }

    private async Task SendViaResendAsync(string apiKey, string fromEmail, string toEmail, string subject, string htmlBody)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            from = $"SecureShop <{fromEmail}>",
            to = new[] { toEmail },
            subject = subject,
            html = htmlBody
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("https://api.resend.com/emails", content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Resend API failed: {StatusCode} - {Error}", response.StatusCode, error);
            throw new InvalidOperationException($"Failed to send email via Resend: {error}");
        }

        _logger.LogInformation("Email sent successfully via Resend to {Email}", toEmail);
    }

    private async Task SendViaSmtpAsync(string toEmail, string subject, string htmlBody)
    {
        var smtpHost = _config["Email:SmtpHost"];
        var smtpPort = int.Parse(_config["Email:SmtpPort"] ?? "587");
        var smtpUser = _config["Email:SmtpUser"];
        var smtpPass = _config["Email:SmtpPassword"];
        var fromName = _config["Email:FromName"] ?? "SecureShop";
        
        // FIX: Ensure fromEmail is never null to satisfy the compiler
        var fromEmail = smtpUser ?? "noreply@secureshop.com";

        if (string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass))
        {
            _logger.LogWarning("Email not sent to {Email} — SMTP credentials not configured.", toEmail);
            return;
        }

        using var client = new SmtpClient(smtpHost, smtpPort)
        {
            Credentials = new NetworkCredential(smtpUser, smtpPass),
            EnableSsl = true
        };

        var mailMessage = new MailMessage
        {
            From = new MailAddress(fromEmail, fromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        mailMessage.To.Add(toEmail);

        await client.SendMailAsync(mailMessage);
        _logger.LogInformation("Email sent successfully via SMTP to {Email}", toEmail);
    }
}