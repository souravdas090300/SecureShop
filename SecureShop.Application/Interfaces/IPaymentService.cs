namespace SecureShop.Application.Interfaces;

/// <summary>
/// Abstracts the external payment gateway (currently Stripe).
/// Using an interface keeps the domain and application layers
/// decoupled from Stripe's SDK.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Creates a payment intent on the payment gateway and returns its ID and client secret.
    /// </summary>
    /// <param name="amount">Charge amount in the smallest currency unit (e.g. cents for USD).</param>
    /// <param name="currency">ISO 4217 currency code (e.g. <c>"usd"</c>).</param>
    /// <param name="orderId">Internal order ID added as metadata on the payment intent.</param>
    /// <returns>The payment intent ID and client secret used by the front-end SDK.</returns>
    Task<PaymentIntentDetails> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId);

    /// <summary>Cancels a test-mode PaymentIntent that could not be attached to a saved order.</summary>
    Task CancelPaymentIntentAsync(string paymentIntentId);
}

/// <summary>Identifiers returned when a Stripe PaymentIntent is created.</summary>
public sealed record PaymentIntentDetails(string Id, string ClientSecret);