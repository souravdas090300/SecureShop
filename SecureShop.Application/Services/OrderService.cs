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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStripeEventRepository _stripeEventRepo;
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
        IUnitOfWork unitOfWork,
        IStripeEventRepository stripeEventRepo,
        ILogger<OrderService> logger)
    {
        _orderRepo = orderRepo;
        _productRepo = productRepo;
        _paymentService = paymentService;
        _cache = cache;
        _unitOfWork = unitOfWork;
        _stripeEventRepo = stripeEventRepo;
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
    /// The status change is an atomic conditional transition so concurrent
    /// deliveries cannot double-apply it.
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

        if (order.Status == Domain.Enums.OrderStatus.PaymentProcessing)
        {
            var transitioned = await _orderRepo.TryTransitionStatusAsync(
                order.Id,
                Domain.Enums.OrderStatus.PaymentProcessing,
                Domain.Enums.OrderStatus.Paid,
                cancellationToken);
            if (transitioned)
                return true;

            // Lost the race — read the committed status fresh (the tracked
            // entity is stale) to see which transition won.
            var current = await _orderRepo.GetStatusByPaymentIntentIdAsync(paymentIntentId, cancellationToken);
            if (current == Domain.Enums.OrderStatus.Paid)
                return true;
            if (current == Domain.Enums.OrderStatus.Cancelled && order is not null)
            {
                await RefundLatePaymentAsync(order, cancellationToken);
                return false;
            }
            return false;
        }

        if (order.Status == Domain.Enums.OrderStatus.Cancelled)
        {
            // Money arrived for an order we already released — refund it.
            await RefundLatePaymentAsync(order, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Processes a verified Stripe webhook event exactly once. The event-id
    /// insert and all of its effects commit in a single transaction; a
    /// duplicate delivery returns without doing anything, and transient
    /// failures propagate so Stripe retries with a 5xx.
    /// </summary>
    /// <param name="eventId">Stripe event ID (evt_...) used for deduplication.</param>
    /// <param name="eventType">Stripe event type.</param>
    /// <param name="paymentIntentId">The PaymentIntent the event refers to.</param>
    /// <param name="amount">Expected amount in minor units (from the event).</param>
    /// <param name="amountReceived">Amount actually received, in minor units.</param>
    /// <param name="currency">Currency code from the event.</param>
    /// <param name="failureCode">Stripe failure code for payment_failed events.</param>
    public async Task ProcessStripeEventAsync(
        string eventId,
        string eventType,
        string paymentIntentId,
        long amount,
        long amountReceived,
        string currency,
        string? failureCode,
        CancellationToken cancellationToken = default)
    {
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Claim the event first — ON CONFLICT DO NOTHING means a duplicate
            // or parallel delivery sees 0 rows inserted and does no work.
            if (!await _stripeEventRepo.TryRecordProcessedEventAsync(eventId, eventType, cancellationToken))
                return false;

            switch (eventType)
            {
                case "payment_intent.succeeded":
                    await ConfirmPaymentAsync(paymentIntentId, amount, amountReceived, currency, cancellationToken);
                    break;
                case "payment_intent.payment_failed":
                    await RecordPaymentFailureAsync(paymentIntentId, failureCode, cancellationToken);
                    break;
                case "payment_intent.canceled":
                    await CancelUnpaidOrderCoreAsync(paymentIntentId, cancellationToken);
                    break;
            }

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Cancels an order still awaiting payment and restores its reserved stock.
    /// Used by the abandonment job after Stripe confirms the intent is cancelled.
    /// Safe to call concurrently — the status transition is atomic, so stock is
    /// restored exactly once.
    /// </summary>
    /// <returns><c>true</c> when this call performed the cancellation.</returns>
    public async Task<bool> CancelUnpaidOrderAsync(string paymentIntentId, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteInTransactionAsync(
            () => CancelUnpaidOrderCoreAsync(paymentIntentId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Returns orders stuck in PaymentProcessing since before the cutoff —
    /// candidates for the abandonment sweep.
    /// </summary>
    public async Task<IEnumerable<OrderResponseDto>> GetAbandonedOrdersAsync(
        DateTime createdBefore, int take, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepo.GetStalePaymentProcessingOrdersAsync(createdBefore, take, cancellationToken);
        return orders
            .Where(o => !string.IsNullOrEmpty(o.StripePaymentIntentId))
            .Select(o => new OrderResponseDto(
                o.Id, o.UserId, o.User?.Email ?? string.Empty,
                o.Items.Select(i => new OrderItemResponseDto(
                    i.ProductId, i.Product?.Name ?? string.Empty, i.Quantity, i.UnitPrice)).ToList(),
                o.Status, o.TotalAmount, o.StripePaymentIntentId, o.CreatedAt, null))
            .ToList();
    }

    /// <summary>
    /// Records a failed payment attempt on the order. The stock reservation is
    /// kept because the PaymentIntent stays retryable after payment_failed.
    /// </summary>
    private async Task RecordPaymentFailureAsync(string paymentIntentId, string? failureCode, CancellationToken cancellationToken)
    {
        var order = await _orderRepo.GetByPaymentIntentIdAsync(paymentIntentId, cancellationToken);
        if (order is null || order.Status != Domain.Enums.OrderStatus.PaymentProcessing)
            return;

        order.RecordPaymentFailure(failureCode, DateTime.UtcNow);
        await _orderRepo.UpdateAsync(order, cancellationToken);
    }

    /// <summary>
    /// Transitions PaymentProcessing → Cancelled atomically and restores every
    /// reserved line, but only when this call wins the transition. Paid and
    /// already-Cancelled orders are left untouched. Must run inside a
    /// transaction (the webhook wrapper or <see cref="CancelUnpaidOrderAsync"/>).
    /// </summary>
    private async Task<bool> CancelUnpaidOrderCoreAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        var order = await _orderRepo.GetByPaymentIntentIdAsync(paymentIntentId, cancellationToken);
        if (order is null || order.Status != Domain.Enums.OrderStatus.PaymentProcessing)
            return false;

        var transitioned = await _orderRepo.TryTransitionStatusAsync(
            order.Id,
            Domain.Enums.OrderStatus.PaymentProcessing,
            Domain.Enums.OrderStatus.Cancelled,
            cancellationToken);
        if (!transitioned)
            return false; // a concurrent event already moved the order (e.g. to Paid)

        foreach (var item in order.Items)
            await _productRepo.RestoreStockAsync(item.ProductId, item.Quantity, cancellationToken);
        foreach (var item in order.Items)
            await EvictProductCacheAsync(item.ProductId);

        _logger.LogInformation("Order {OrderId} cancelled after payment intent {PaymentIntentId} was cancelled; stock restored", order.Id, PaymentIntentIdHelper.Mask(paymentIntentId));
        return true;
    }

    /// <summary>
    /// Refunds a payment that succeeded after its order was already cancelled,
    /// and stores the Stripe refund ID on the order. Idempotent per order via
    /// the Stripe idempotency key and the StripeRefundId guard.
    /// </summary>
    private async Task RefundLatePaymentAsync(Order order, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(order.StripePaymentIntentId) || order.StripeRefundId is not null)
            return;

        var refundId = await _paymentService.RefundPaymentIntentAsync(
            order.StripePaymentIntentId,
            $"secure-shop-refund-{order.Id}");
        order.SetRefundId(refundId);
        await _orderRepo.UpdateAsync(order, cancellationToken);

        _logger.LogWarning(
            "Late payment on cancelled order {OrderId}: payment intent {PaymentIntentId} succeeded after cancellation; refund {RefundId} issued",
            order.Id, PaymentIntentIdHelper.Mask(order.StripePaymentIntentId), refundId);
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
