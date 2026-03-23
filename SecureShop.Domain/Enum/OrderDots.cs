using SecureShop.Domain.Enums;

namespace SecureShop.Domain.Enum;

public record DomainCreateOrderItemDto(Guid ProductId, int Quantity);
public record DomainCreateOrderDto(List<DomainCreateOrderItemDto> Items);

public record DomainOrderItemResponseDto(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice);

public record DomainOrderResponseDto(
    Guid Id,
    string UserId,
    List<DomainOrderItemResponseDto> Items,
    OrderStatus Status,
    decimal TotalAmount,
    string? StripePaymentIntentId,
    DateTime CreatedAt);