using SecureShop.Application.Interfaces;

namespace SecureShop.IntegrationTests;

/// <summary>
/// Deterministic <see cref="IPaymentService"/> fake for integration tests.
/// Intent states can be programmed per intent ID; every call is recorded so
/// tests can assert cancellation/refund behaviour without Stripe.
/// </summary>
public class FakePaymentService : IPaymentService
{
    private readonly Dictionary<string, PaymentIntentState> _states = new();
    private int _counter;

    /// <summary>PaymentIntent IDs passed to <see cref="CancelPaymentIntentAsync"/>.</summary>
    public List<string> CancelledIntents { get; } = new();

    /// <summary>(paymentIntentId, idempotencyKey) pairs passed to <see cref="RefundPaymentIntentAsync"/>.</summary>
    public List<(string IntentId, string IdempotencyKey)> Refunds { get; } = new();

    /// <summary>Intent IDs that should make <see cref="GetPaymentIntentStatusAsync"/> throw (Stripe outage simulation).</summary>
    public HashSet<string> FailingIntents { get; } = new();

    /// <summary>
    /// Programs the state reported by <see cref="GetPaymentIntentStatusAsync"/>
    /// for an intent. Defaults to <see cref="PaymentIntentState.RequiresPaymentMethod"/>.
    /// </summary>
    public void SetIntentState(string paymentIntentId, PaymentIntentState state) =>
        _states[paymentIntentId] = state;

    /// <inheritdoc />
    public Task<PaymentIntentDetails> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId)
        => Task.FromResult(new PaymentIntentDetails(
            $"pi_test_{Interlocked.Increment(ref _counter):D6}_{Guid.NewGuid():N}",
            "secret"));

    /// <inheritdoc />
    public Task CancelPaymentIntentAsync(string paymentIntentId)
    {
        CancelledIntents.Add(paymentIntentId);
        _states[paymentIntentId] = PaymentIntentState.Canceled;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PaymentIntentState> GetPaymentIntentStatusAsync(string paymentIntentId)
    {
        if (FailingIntents.Contains(paymentIntentId))
            throw new InvalidOperationException($"Simulated Stripe failure for {paymentIntentId}");
        if (_states.TryGetValue(paymentIntentId, out var state))
            return Task.FromResult(state);
        return Task.FromResult(PaymentIntentState.RequiresPaymentMethod);
    }

    /// <inheritdoc />
    public Task<string> RefundPaymentIntentAsync(string paymentIntentId, string idempotencyKey)
    {
        Refunds.Add((paymentIntentId, idempotencyKey));
        return Task.FromResult($"re_test_{Guid.NewGuid():N}");
    }
}
