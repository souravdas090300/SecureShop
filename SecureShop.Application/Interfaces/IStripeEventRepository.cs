namespace SecureShop.Application.Interfaces;

/// <summary>
/// Data-access contract for the ProcessedStripeEvents idempotency table.
/// </summary>
public interface IStripeEventRepository
{
    /// <summary>
    /// Attempts to record a Stripe event as processed. Returns <c>true</c> when
    /// this is the first delivery; <c>false</c> when the event was already
    /// recorded (duplicate delivery — caller must do nothing).
    /// Must be called inside a transaction so the marker and the event's effects
    /// commit or roll back together.
    /// </summary>
    Task<bool> TryRecordProcessedEventAsync(string eventId, string eventType, CancellationToken ct = default);
}
