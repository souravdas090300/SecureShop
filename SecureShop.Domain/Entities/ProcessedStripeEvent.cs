namespace SecureShop.Domain.Entities;

/// <summary>
/// Records a Stripe webhook event that has been fully processed.
/// The EventId primary key is the idempotency guarantee: a duplicate
/// delivery fails the insert and is ignored.
/// </summary>
public class ProcessedStripeEvent
{
    /// <summary>Stripe event ID (evt_...), unique per event.</summary>
    public string EventId { get; private set; } = string.Empty;

    /// <summary>The Stripe event type that was processed (e.g. payment_intent.succeeded).</summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>UTC timestamp when the event was recorded.</summary>
    public DateTime ProcessedAt { get; private set; }

    // EF Core requires a parameterless constructor for materialisation.
    private ProcessedStripeEvent() { }

    /// <summary>Creates a record for a successfully processed Stripe event.</summary>
    public static ProcessedStripeEvent Create(string eventId, string type) => new()
    {
        EventId = eventId,
        Type = type,
        ProcessedAt = DateTime.UtcNow
    };
}
