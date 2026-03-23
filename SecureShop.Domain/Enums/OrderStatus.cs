namespace SecureShop.Domain.Enums;

public enum OrderStatus
{
    Pending,
    PaymentProcessing,
    Paid,
    Shipped,
    Delivered,
    Cancelled
}
