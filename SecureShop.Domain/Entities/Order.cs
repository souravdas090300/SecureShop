using SecureShop.Domain.Enums;
using SecureShop.Domain.Exceptions;

namespace SecureShop.Domain.Entities;

/// <summary>
/// Aggregate root representing a customer purchase order.
/// All state changes go through domain methods to enforce business rules
/// (e.g. an order cannot be cancelled once shipped).
/// </summary>
public class Order
{
    /// <summary>Unique identifier for the order.</summary>
    public Guid Id { get; private set; }

    /// <summary>ID of the <see cref="ApplicationUser"/> who placed the order.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Navigation property to the owning user.</summary>
    public ApplicationUser User { get; private set; } = null!;

    /// <summary>The individual line items that make up this order.</summary>
    public List<OrderItem> Items { get; private set; } = new();

    /// <summary>Current lifecycle status of the order.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>Sum of (unit price × quantity) for all items; calculated once at creation.</summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>Stripe payment intent ID, set when payment processing begins.</summary>
    public string? StripePaymentIntentId { get; private set; }

    /// <summary>UTC timestamp when the order was first created.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>UTC timestamp of the most recent state change.</summary>
    public DateTime UpdatedAt { get; private set; }

    // EF Core requires a parameterless constructor for materialisation.
    private Order() { }

    /// <summary>
    /// Factory method — creates a new <see cref="Order"/> in <see cref="OrderStatus.Pending"/> state.
    /// </summary>
    /// <param name="userId">Identity of the customer placing the order.</param>
    /// <param name="items">Non-empty list of items to include in the order.</param>
    /// <returns>A fully initialised <see cref="Order"/>.</returns>
    /// <exception cref="DomainException">Thrown when <paramref name="items"/> is empty.</exception>
    public static Order Create(string userId, List<OrderItem> items)
    {
        if (!items.Any()) throw new DomainException("Order must have at least one item");

        return new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Items = items,
            Status = OrderStatus.Pending,
            // Total is fixed at creation time so price changes don't affect historical orders.
            TotalAmount = items.Sum(i => i.UnitPrice * i.Quantity),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Associates a Stripe payment intent with this order and advances status
    /// to <see cref="OrderStatus.PaymentProcessing"/>.
    /// </summary>
    /// <param name="paymentIntentId">The PaymentIntent ID returned by Stripe.</param>
    public void SetPaymentIntent(string paymentIntentId)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId))
            throw new DomainException("Stripe payment intent ID is required");

        StripePaymentIntentId = paymentIntentId;
        Status = OrderStatus.PaymentProcessing;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Marks the order as <see cref="OrderStatus.Paid"/> after payment confirmation.</summary>
    public void MarkAsPaid()
    {
        if (Status == OrderStatus.Paid)
            return;

        if (Status != OrderStatus.PaymentProcessing)
            throw new DomainException("Only an order awaiting payment can be marked as paid");

        Status = OrderStatus.Paid;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Cancels the order. Shipment or delivery already in progress cannot be cancelled.
    /// </summary>
    /// <exception cref="DomainException">
    /// Thrown when payment requires cancellation/refund or the order is already shipped/delivered.
    /// </exception>
    public void Cancel()
    {
        if (Status == OrderStatus.Shipped || Status == OrderStatus.Delivered)
            throw new DomainException("Cannot cancel a shipped or delivered order");
        if (Status is OrderStatus.PaymentProcessing or OrderStatus.Paid)
            throw new DomainException("Orders with a payment intent require verified payment cancellation or refund");
        Status = OrderStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Advances an order through an allowed fulfillment status transition.
    /// Payment status can only be changed by verified payment confirmation.
    /// </summary>
    /// <param name="status">The target status to apply.</param>
    public void SetStatus(OrderStatus status)
    {
        if (!Enum.IsDefined(typeof(OrderStatus), status))
            throw new DomainException("Invalid order status");
        if (status == OrderStatus.Paid)
            throw new DomainException("Only a verified payment can mark an order as paid");
        if (status == OrderStatus.Shipped && Status != OrderStatus.Paid)
            throw new DomainException("Only paid orders can be shipped");
        if (status == OrderStatus.Delivered && Status != OrderStatus.Shipped)
            throw new DomainException("Only shipped orders can be delivered");
        if (status == OrderStatus.Cancelled &&
            (Status != OrderStatus.Pending || StripePaymentIntentId is not null))
        {
            throw new DomainException("Orders with a payment intent require verified payment cancellation");
        }
        if (status is OrderStatus.Pending or OrderStatus.PaymentProcessing)
            throw new DomainException("Order status cannot be reset");

        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }
}