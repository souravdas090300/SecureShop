using SecureShop.Domain.Enums;
using SecureShop.Domain.Exceptions;

namespace SecureShop.Domain.Entities;

public class Order
{
    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public ApplicationUser User { get; private set; } = null!;
    public List<OrderItem> Items { get; private set; } = new();
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string? StripePaymentIntentId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Order() { }

    public static Order Create(string userId, List<OrderItem> items)
    {
        if (!items.Any()) throw new DomainException("Order must have at least one item");

        return new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Items = items,
            Status = OrderStatus.Pending,
            TotalAmount = items.Sum(i => i.UnitPrice * i.Quantity),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void SetPaymentIntent(string paymentIntentId)
    {
        StripePaymentIntentId = paymentIntentId;
        Status = OrderStatus.PaymentProcessing;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsPaid()
    {
        Status = OrderStatus.Paid;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Shipped || Status == OrderStatus.Delivered)
            throw new DomainException("Cannot cancel a shipped or delivered order");
        Status = OrderStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
    }
}