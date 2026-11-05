using Microsoft.Extensions.Logging;
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
    private readonly ICacheService _cache;
    private readonly ILogger<OrderService> _logger;

    // Matches the key prefix used by ProductService for individual product DTOs.
    private const string ProductCachePrefix = "product:";

    /// <summary>
    /// Initialises the service with its required dependencies.
    /// All are registered as scoped services via DI.
    /// </summary>
    public OrderService(
        IOrderRepository orderRepo,
        IProductRepository productRepo,
        IPaymentService paymentService,
        ICacheService cache,
        ILogger<OrderService> logger)
    {
        _orderRepo = orderRepo;
        _productRepo = productRepo;
        _paymentService = paymentService;
        _cache = cache;
        _logger = logger;
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
        if (dto.Items is null || dto.Items.Count == 0 || dto.Items.Any(item => item.Quantity <= 0))
            throw new DomainException("Order must contain items with a positive quantity");

        var quantities = dto.Items
            .GroupBy(item => item.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                Quantity = group.Sum(item => (long)item.Quantity)
            })
            // Deterministic reservation order prevents deadlocks if these updates
            // are ever wrapped in a transaction.
            .OrderBy(item => item.ProductId)
            .ToList();
        if (quantities.Any(item => item.Quantity > int.MaxValue))
            throw new DomainException("Requested quantity is too large");

        var items = new List<OrderItem>();
        var productsToReserve = new List<(Product Product, int Quantity)>();

        // Read each product once for its price; stock is reserved atomically below.
        foreach (var item in quantities)
        {
            var product = await _productRepo.GetByIdAsync(item.ProductId)
                ?? throw new DomainException($"Product {item.ProductId} not found");
            var quantity = (int)item.Quantity;

            // Capture the unit price at the time of order so later price changes
            // do not affect historical order totals.
            items.Add(OrderItem.Create(product.Id, quantity, product.Price));
            productsToReserve.Add((product, quantity));
        }

        // Build the domain aggregate and create its Stripe payment intent.
        var order = Order.Create(userId, items);
        PaymentIntentDetails? paymentIntent = null;
        var reservedProducts = new List<(Guid ProductId, int Quantity)>();
        try
        {
            foreach (var (product, quantity) in productsToReserve)
            {
                // Atomic conditional decrement: the database enforces
                // StockQuantity >= quantity in the same statement, so concurrent
                // orders cannot oversell the last units.
                if (!await _productRepo.TryReserveStockAsync(product.Id, quantity))
                    throw new DomainException($"Insufficient stock for product {product.Name}");
                reservedProducts.Add((product.Id, quantity));

                // ExecuteUpdate bypasses the change tracker; drop the cached DTO.
                // product:list:* entries may show stale stock for up to 5 minutes —
                // acceptable because checkout validates stock atomically against
                // the database, not the cache.
                await EvictProductCacheAsync(product.Id);
            }

            paymentIntent = await _paymentService.CreatePaymentIntentAsync(
                order.TotalAmount,
                "usd",
                order.Id);

            order.SetPaymentIntent(paymentIntent.Id);
            await _orderRepo.CreateAsync(order);
        }
        catch (Exception operationError)
        {
            var cleanupErrors = new List<Exception>();
            if (paymentIntent is not null)
            {
                try
                {
                    await _paymentService.CancelPaymentIntentAsync(paymentIntent.Id);
                }
                catch (Exception cleanupError)
                {
                    cleanupErrors.Add(cleanupError);
                }
            }

            // Restore every reserved line first; cache eviction runs afterwards so
            // a cache failure can never skip a stock restore.
            foreach (var (productId, quantity) in reservedProducts.AsEnumerable().Reverse())
            {
                try
                {
                    await _productRepo.RestoreStockAsync(productId, quantity);
                }
                catch (Exception cleanupError)
                {
                    cleanupErrors.Add(cleanupError);
                }
            }

            foreach (var (productId, _) in reservedProducts)
                await EvictProductCacheAsync(productId);

            if (cleanupErrors.Count > 0)
                throw new AggregateException("Order setup failed and cleanup was incomplete", [operationError, .. cleanupErrors]);

            throw;
        }

        return MapToDto(order, paymentIntent!.ClientSecret);
    }

    /// <summary>
    /// Marks an order paid only when a verified Stripe event matches its
    /// persisted PaymentIntent ID, expected amount, and currency.
    /// </summary>
    public async Task<bool> ConfirmPaymentAsync(
        string paymentIntentId,
        long amount,
        long amountReceived,
        string currency,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepo.GetByPaymentIntentIdAsync(paymentIntentId, cancellationToken);
        if (order is null ||
            !string.Equals(currency, "usd", StringComparison.OrdinalIgnoreCase) ||
            amount != ToMinorUnits(order.TotalAmount) ||
            amountReceived != ToMinorUnits(order.TotalAmount))
        {
            return false;
        }

        if (order.Status == Domain.Enums.OrderStatus.Paid)
            return true;

        if (order.Status != Domain.Enums.OrderStatus.PaymentProcessing)
            return false;

        order.MarkAsPaid();
        await _orderRepo.UpdateAsync(order, cancellationToken);
        return true;
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

        if (!Enum.IsDefined(typeof(Domain.Enums.OrderStatus), status))
            throw new DomainException("Invalid order status");

        order.SetStatus((Domain.Enums.OrderStatus)status);
        await _orderRepo.UpdateAsync(order);
    }

    /// <summary>Returns all orders belonging to the specified user, mapped to DTOs.</summary>
    /// <param name="userId">Identity of the requesting user.</param>
    public async Task<IEnumerable<OrderResponseDto>> GetMyOrdersAsync(string userId)
    {
        var orders = await _orderRepo.GetByUserIdAsync(userId);
        return orders.Select(order => MapToDto(order));
    }

    /// <summary>Returns every order in the system; intended for admin use only.</summary>
    public async Task<IEnumerable<OrderResponseDto>> GetAllOrdersAsync()
    {
        var orders = await _orderRepo.GetAllAsync();
        return orders.Select(order => MapToDto(order));
    }

    /// <summary>
    /// Maps an <see cref="Order"/> domain aggregate to its response DTO.
    /// Navigation properties (User, Product) may be null if not eagerly loaded—
    /// safe defaults are used in that case.
    /// </summary>
    private static OrderResponseDto MapToDto(Order o, string? paymentClientSecret = null) => new(
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
        o.CreatedAt,
        paymentClientSecret);

    /// <summary>
    /// Removes the cached product DTO after a stock change. Cache failures are
    /// logged and swallowed — a stale cache entry is harmless because stock is
    /// always validated atomically against the database at checkout.
    /// </summary>
    private async Task EvictProductCacheAsync(Guid productId)
    {
        try
        {
            await _cache.RemoveAsync($"{ProductCachePrefix}{productId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evict cached product {ProductId} after stock change", productId);
        }
    }

    private static long ToMinorUnits(decimal amount) =>
        checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
}
