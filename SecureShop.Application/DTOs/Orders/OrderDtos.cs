using SecureShop.Domain.Enums;

namespace SecureShop.Application.DTOs.Orders;

/// <summary>A single line item in an order request: which product and how many.</summary>
public record OrderItemDto(Guid ProductId, int Quantity);

/// <summary>Request body for creating a new order; contains one or more line items.</summary>
public record CreateOrderDto(List<OrderItemDto> Items);

/// <summary>Read-model for a single order line item, enriched with the product name and price at purchase time.</summary>
public record OrderItemResponseDto(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice);

/// <summary>Admin request body for advancing an order's lifecycle status.</summary>
public record UpdateOrderStatusDto(int Status);

/// <summary>
/// Full read-model for an order, returned by GET and POST order endpoints.
/// Includes all line items, the Stripe client secret (when payment is pending),
/// and the order's current lifecycle status.
/// </summary>
public record OrderResponseDto(
    Guid Id,
    string UserId,
    string CustomerEmail,
    List<OrderItemResponseDto> Items,
    OrderStatus Status,
    decimal TotalAmount,
    string? StripePaymentIntentId,
    DateTime CreatedAt);