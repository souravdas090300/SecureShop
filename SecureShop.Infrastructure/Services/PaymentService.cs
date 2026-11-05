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
    private readonly IConfiguration _configuration;

    /// <summary>Reads Stripe settings from application configuration.</summary>
    public PaymentService(IConfiguration configuration) => _configuration = configuration;

    /// <inheritdoc />
    public async Task<PaymentIntentDetails> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(currency))
            throw new ArgumentOutOfRangeException(nameof(amount), "A positive amount and currency are required.");

        var options = new PaymentIntentCreateOptions
        {
            Amount = checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero)),
            Currency = currency,
            Metadata = new Dictionary<string, string>
            {
                { "orderId", orderId.ToString() }
            }
        };

        var service = new PaymentIntentService(CreateClient());
        var intent = await service.CreateAsync(options, new RequestOptions
        {
            IdempotencyKey = $"secure-shop-order-{orderId}"
        });

        return new PaymentIntentDetails(intent.Id, intent.ClientSecret);
    }

    /// <inheritdoc />
    public async Task CancelPaymentIntentAsync(string paymentIntentId)
    {
        await new PaymentIntentService(CreateClient()).CancelAsync(paymentIntentId);
    }

    /// <inheritdoc />
    public async Task<PaymentIntentState> GetPaymentIntentStatusAsync(string paymentIntentId)
    {
        var intent = await new PaymentIntentService(CreateClient()).GetAsync(paymentIntentId);
        return intent.Status switch
        {
            "requires_payment_method" => PaymentIntentState.RequiresPaymentMethod,
            "requires_confirmation" => PaymentIntentState.RequiresConfirmation,
            "requires_action" => PaymentIntentState.RequiresAction,
            "processing" => PaymentIntentState.Processing,
            "requires_capture" => PaymentIntentState.RequiresCapture,
            "succeeded" => PaymentIntentState.Succeeded,
            "canceled" => PaymentIntentState.Canceled,
            _ => PaymentIntentState.Unknown
        };
    }

    /// <inheritdoc />
    public async Task<string> RefundPaymentIntentAsync(string paymentIntentId, string idempotencyKey)
    {
        var refund = await new RefundService(CreateClient()).CreateAsync(
            new RefundCreateOptions { PaymentIntent = paymentIntentId },
            new RequestOptions { IdempotencyKey = idempotencyKey });
        return refund.Id;
    }

    private StripeClient CreateClient()
    {
        var secretKey = _configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey) ||
            !secretKey.StartsWith("sk_test_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Stripe test-mode payments require a Stripe:SecretKey beginning with sk_test_.");
        }

        return new StripeClient(secretKey);
    }
}
