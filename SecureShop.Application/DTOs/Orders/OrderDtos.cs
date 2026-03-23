using SecureShop.Domain.Enums;

namespace SecureShop.Application.DTOs.Orders;

public record OrderItemDto(Guid ProductId, int Quantity);
public record CreateOrderDto(List<OrderItemDto> Items);

public record OrderItemResponseDto(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice);

public record OrderResponseDto(
    Guid Id,
    string UserId,
    List<OrderItemResponseDto> Items,
    OrderStatus Status,
    decimal TotalAmount,
    string? StripePaymentIntentId,
    DateTime CreatedAt);