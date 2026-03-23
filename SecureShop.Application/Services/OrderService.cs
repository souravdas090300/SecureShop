using SecureShop.Application.DTOs.Orders;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Exceptions;

namespace SecureShop.Application.Services;

public class OrderService
{
    private readonly IOrderRepository _orderRepo;
    private readonly IProductRepository _productRepo;
    private readonly IPaymentService _paymentService;

    public OrderService(
        IOrderRepository orderRepo,
        IProductRepository productRepo,
        IPaymentService paymentService)
    {
        _orderRepo = orderRepo;
        _productRepo = productRepo;
        _paymentService = paymentService;
    }

    public async Task<OrderResponseDto> CreateAsync(CreateOrderDto dto, string userId)
    {
        var items = new List<OrderItem>();

        foreach (var item in dto.Items)
        {
            var product = await _productRepo.GetByIdAsync(item.ProductId)
                ?? throw new DomainException($"Product {item.ProductId} not found");

            product.ReduceStock(item.Quantity);
            await _productRepo.UpdateAsync(product);

            items.Add(OrderItem.Create(product.Id, item.Quantity, product.Price));
        }

        var order = Order.Create(userId, items);
        var clientSecret = await _paymentService.CreatePaymentIntentAsync(
            order.TotalAmount,
            "usd",
            order.Id);

        order.SetPaymentIntent(clientSecret);
        await _orderRepo.CreateAsync(order);

        return MapToDto(order);
    }

    public async Task<OrderResponseDto> GetByIdAsync(Guid id, string userId)
    {
        var order = await _orderRepo.GetByIdAsync(id)
            ?? throw new DomainException($"Order {id} not found");

        if (order.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        return MapToDto(order);
    }

    public async Task<IEnumerable<OrderResponseDto>> GetMyOrdersAsync(string userId)
    {
        var orders = await _orderRepo.GetByUserIdAsync(userId);
        return orders.Select(MapToDto);
    }

    private static OrderResponseDto MapToDto(Order o) => new(
        o.Id,
        o.UserId,
        o.Items.Select(i => new OrderItemResponseDto(
            i.ProductId,
            i.Product?.Name ?? string.Empty,
            i.Quantity,
            i.UnitPrice)).ToList(),
        o.Status,
        o.TotalAmount,
        o.StripePaymentIntentId,
        o.CreatedAt);
}
