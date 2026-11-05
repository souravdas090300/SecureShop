using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureShop.Application.Services;
using Stripe;

namespace SecureShop.API.Controllers;

/// <summary>Receives signed Stripe events and applies verified payment updates.</summary>
[ApiController]
[Route("api/webhooks/stripe")]
[AllowAnonymous]
public sealed class StripeWebhookController : ControllerBase
{
    private readonly OrderService _orderService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StripeWebhookController> _logger;

    /// <summary>Creates the webhook controller.</summary>
    public StripeWebhookController(
        OrderService orderService,
        IConfiguration configuration,
        ILogger<StripeWebhookController> logger)
    {
        _orderService = orderService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Verifies Stripe's signature and applies successful payment events.</summary>
    /// <param name="cancellationToken">Request cancellation token.</param>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var webhookSecret = _configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            _logger.LogError("Stripe webhook received but Stripe:WebhookSecret is not configured");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!Request.Headers.TryGetValue("Stripe-Signature", out var signature))
            return BadRequest();

        string payload;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            payload = await reader.ReadToEndAsync(cancellationToken);

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signature, webhookSecret);
        }
        catch (StripeException exception)
        {
            _logger.LogWarning(exception, "Stripe rejected the webhook signature or API-version payload");
            return BadRequest();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            _logger.LogWarning(exception, "Rejected Stripe webhook with an invalid payload");
            return BadRequest();
        }

        if (stripeEvent.Type != "payment_intent.succeeded")
            return Ok();

        if (stripeEvent.Data.Object is not PaymentIntent paymentIntent)
        {
            _logger.LogWarning("Stripe payment success event had an unexpected payload");
            return BadRequest();
        }

        var applied = await _orderService.ConfirmPaymentAsync(
            paymentIntent.Id,
            paymentIntent.Amount,
            paymentIntent.AmountReceived,
            paymentIntent.Currency,
            cancellationToken);

        if (!applied)
        {
            _logger.LogWarning(
                "Stripe payment intent {PaymentIntentId} did not match a payable order and was not applied",
                paymentIntent.Id);
        }

        return Ok();
    }
}
