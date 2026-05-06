using SecureShop.Application.DTOs.Orders;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Exceptions;

namespace SecureShop.Application.Services;

/// <summary>
/// Application-layer service that orchestrates order creation, retrieval,
/// and status updates. Coordinates the product repository (for stock reduction),
/// the payment service (for Stripe intent creation), and the order repository.
/// </summary>
public class OrderService
{
    private readonly IOrderRepository _orderRepo;
    private readonly IProductRepository _productRepo;
    private readonly IPaymentService _paymentService;

    /// <summary>
    /// Initialises the service with its required dependencies.
    /// All three are registered as scoped services via DI.
    /// </summary>
    public OrderService(
        IOrderRepository orderRepo,
        IProductRepository productRepo,
        IPaymentService paymentService)
    {
        _orderRepo = orderRepo;
        _productRepo = productRepo;
        _paymentService = paymentService;
    }

    /// <summary>
    /// Creates a new order for the given user.
    /// For each requested item, the product stock is reduced and a Stripe
    /// payment intent is created before persisting the order.
    /// </summary>
    /// <param name="dto">Order creation request containing the list of items.</param>
    /// <param name="userId">Identity of the authenticated user placing the order.</param>
    /// <returns>A response DTO representing the newly created order.</returns>
    /// <exception cref="DomainException">
    /// Thrown when a product is not found or has insufficient stock.
    /// </exception>
    public async Task<OrderResponseDto> CreateAsync(CreateOrderDto dto, string userId)
    {
        var items = new List<OrderItem>();

        // Validate and reserve stock for every requested line item.
        foreach (var item in dto.Items)
        {
            var product = await _productRepo.GetByIdAsync(item.ProductId)
                ?? throw new DomainException($"Product {item.ProductId} not found");

            // ReduceStock enforces the invariant that stock cannot go negative.
            product.ReduceStock(item.Quantity);
            await _productRepo.UpdateAsync(product);

            // Capture the unit price at the time of order so later price changes
            // do not affect historical order totals.
            items.Add(OrderItem.Create(product.Id, item.Quantity, product.Price));
        }

        // Build the domain aggregate and get a Stripe payment intent client secret.
        var order = Order.Create(userId, items);
        var clientSecret = await _paymentService.CreatePaymentIntentAsync(
            order.TotalAmount,
            "usd",
            order.Id);

        // Attach the payment intent and persist the order.
        order.SetPaymentIntent(clientSecret);
        await _orderRepo.CreateAsync(order);

        return MapToDto(order);
    }

    /// <summary>
    /// Retrieves a single order by ID.
    /// When <paramref name="userId"/> is non-null, access is restricted to the
    /// order owner (admin callers pass <c>null</c> to bypass the check).
    /// </summary>
    /// <param name="id">Order identifier.</param>
    /// <param name="userId">
    /// ID of the requesting user, or <c>null</c> to allow admin access to any order.
    /// </param>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Order not found.</exception>
    /// <exception cref="System.UnauthorizedAccessException">
    /// The requesting user does not own the order.
    /// </exception>
    public async Task<OrderResponseDto> GetByIdAsync(Guid id, string? userId)
    {
        var order = await _orderRepo.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Order {id} not found");

        // Enforce ownership: null userId means an admin is calling; skip the check.
        if (userId != null && order.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        return MapToDto(order);
    }

    /// <summary>
    /// Advances an order to the specified status value.
    /// Used by admin endpoints to manually move orders through the lifecycle.
    /// </summary>
    /// <param name="id">Order identifier.</param>
    /// <param name="status">Integer representation of the target <see cref="Domain.Enums.OrderStatus"/>.</param>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Order not found.</exception>
    public async Task UpdateOrderStatusAsync(Guid id, int status)
    {
        var order = await _orderRepo.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Order {id} not found");

        order.SetStatus((Domain.Enums.OrderStatus)status);
        await _orderRepo.UpdateAsync(order);
    }

    /// <summary>Returns all orders belonging to the specified user, mapped to DTOs.</summary>
    /// <param name="userId">Identity of the requesting user.</param>
    public async Task<IEnumerable<OrderResponseDto>> GetMyOrdersAsync(string userId)
    {
        var orders = await _orderRepo.GetByUserIdAsync(userId);
        return orders.Select(MapToDto);
    }

    /// <summary>Returns every order in the system; intended for admin use only.</summary>
    public async Task<IEnumerable<OrderResponseDto>> GetAllOrdersAsync()
    {
        var orders = await _orderRepo.GetAllAsync();
        return orders.Select(MapToDto);
    }

    /// <summary>
    /// Maps an <see cref="Order"/> domain aggregate to its response DTO.
    /// Navigation properties (User, Product) may be null if not eagerly loaded—
    /// safe defaults are used in that case.
    /// </summary>
    private static OrderResponseDto MapToDto(Order o) => new(
        o.Id,
        o.UserId,
        o.User?.Email ?? string.Empty,
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
