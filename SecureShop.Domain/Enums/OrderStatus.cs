namespace SecureShop.Domain.Enums;

/// <summary>
/// Represents the lifecycle states an <see cref="SecureShop.Domain.Entities.Order"/> can pass through,
/// from initial creation to final fulfilment or cancellation.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order created but payment has not yet started.</summary>
    Pending,

    /// <summary>Payment intent created; waiting for payment gateway confirmation.</summary>
    PaymentProcessing,

    /// <summary>Payment confirmed successfully.</summary>
    Paid,

    /// <summary>Order dispatched to the carrier.</summary>
    Shipped,

    /// <summary>Order delivered to the customer.</summary>
    Delivered,

    /// <summary>Order cancelled before shipment.</summary>
    Cancelled
}
