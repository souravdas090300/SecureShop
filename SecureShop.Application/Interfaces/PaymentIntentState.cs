namespace SecureShop.Application.Interfaces;

/// <summary>
/// Coarse-grained state of a Stripe PaymentIntent as reported by the API.
/// Only <see cref="Succeeded"/> and <see cref="Canceled"/> are terminal.
/// </summary>
public enum PaymentIntentState
{
    /// <summary>Intent could not be found or its status was not recognised.</summary>
    Unknown,

    /// <summary>Waiting for a payment method (retryable after a failed attempt).</summary>
    RequiresPaymentMethod,

    /// <summary>Payment method attached; awaiting confirmation.</summary>
    RequiresConfirmation,

    /// <summary>Requires customer action (e.g. 3-D Secure).</summary>
    RequiresAction,

    /// <summary>Payment is being processed.</summary>
    Processing,

    /// <summary>Authorized but not yet captured.</summary>
    RequiresCapture,

    /// <summary>Payment completed successfully — terminal.</summary>
    Succeeded,

    /// <summary>Intent cancelled — terminal.</summary>
    Canceled
}
