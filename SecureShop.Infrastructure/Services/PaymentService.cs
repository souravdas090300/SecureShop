using Microsoft.Extensions.Configuration;
using SecureShop.Application.Interfaces;
using Stripe;

namespace SecureShop.Infrastructure.Services;

/// <summary>
/// Stripe-backed implementation of <see cref="IPaymentService"/>.
/// Converts the order amount to cents (smallest currency unit), attaches
/// the order ID as metadata for webhook reconciliation, and returns the
/// Stripe client secret required by the front-end to confirm the payment.
/// </summary>
public class PaymentService : IPaymentService
{
    /// <summary>
    /// Reads the Stripe secret key from configuration (<c>Stripe:SecretKey</c>)
    /// and configures the global Stripe API client.
    /// </summary>
    public PaymentService(IConfiguration config)
    {
        StripeConfiguration.ApiKey = config["Stripe:SecretKey"];
    }

    /// <inheritdoc />
    public async Task<string> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId)
    {
        var options = new PaymentIntentCreateOptions
        {
            // Stripe expects the amount in the smallest currency unit (cents for USD).
            Amount = (long)(amount * 100),
            Currency = currency,
            // Metadata is returned in Stripe webhooks, enabling server-side order confirmation.
            Metadata = new Dictionary<string, string>
            {
                { "orderId", orderId.ToString() }
            }
        };

        var service = new PaymentIntentService();
        var intent = await service.CreateAsync(options);
        // The client secret is sent to the browser to complete payment via Stripe.js.
        return intent.ClientSecret;
    }
}
