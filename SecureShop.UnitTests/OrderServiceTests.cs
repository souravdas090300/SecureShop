using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SecureShop.Application.DTOs.Orders;
using SecureShop.Application.Interfaces;
using SecureShop.Application.Services;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Enums;
using SecureShop.Domain.Exceptions;
using SecureShop.Infrastructure.Services;

namespace SecureShop.UnitTests;

public class OrderServiceTests
{
    private readonly Mock<IOrderRepository> _orderRepo = new();
    private readonly Mock<IProductRepository> _productRepo = new();
    private readonly Mock<IPaymentService> _payment = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IStripeEventRepository> _stripeEvents = new();
    private OrderService Svc() => new(_orderRepo.Object, _productRepo.Object, _payment.Object, new NullCacheService(), _unitOfWork.Object, _stripeEvents.Object, NullLogger<OrderService>.Instance);

    private static Product MakeProduct(int stock = 10)
        => Product.Create("Widget", "desc", 9.99m, stock, "Electronics");

    private static Order MakeOrder(string userId = "user-1")
    {
        var items = new List<OrderItem> { OrderItem.Create(Guid.NewGuid(), 1, 9.99m) };
        return Order.Create(userId, items);
    }

    // ─── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidItems_CreatesOrderAndReturnsDto()
    {
        var product = MakeProduct();
        var itemDto = new OrderItemDto(product.Id, 2);
        var dto = new CreateOrderDto([itemDto]);

        _productRepo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        _productRepo.Setup(r => r.TryReserveStockAsync(product.Id, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _payment.Setup(p => p.CreatePaymentIntentAsync(It.IsAny<decimal>(), "usd", It.IsAny<Guid>()))
                .ReturnsAsync(new PaymentIntentDetails("pi_test_123", "pi_test_123_secret"));
        _orderRepo.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync((Order o) => o);

        var result = await Svc().CreateAsync(dto, "user-1");

        result.Should().NotBeNull();
        result.UserId.Should().Be("user-1");
        result.TotalAmount.Should().Be(9.99m * 2);
        result.StripePaymentIntentId.Should().Be("pi_test_123");
        result.PaymentClientSecret.Should().Be("pi_test_123_secret");
        _productRepo.Verify(r => r.TryReserveStockAsync(product.Id, 2, It.IsAny<CancellationToken>()), Times.Once);
        _orderRepo.Verify(r => r.CreateAsync(It.IsAny<Order>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ProductNotFound_ThrowsDomainException()
    {
        var missingId = Guid.NewGuid();
        var dto = new CreateOrderDto([new OrderItemDto(missingId, 1)]);
        _productRepo.Setup(r => r.GetByIdAsync(missingId)).ReturnsAsync((Product?)null);

        var act = async () => await Svc().CreateAsync(dto, "user-1");
        await act.Should().ThrowAsync<DomainException>().WithMessage($"*{missingId}*");
    }

    [Fact]
    public async Task CreateAsync_InsufficientStock_ThrowsDomainException()
    {
        var product = MakeProduct(stock: 1);
        var dto = new CreateOrderDto([new OrderItemDto(product.Id, 5)]);
        _productRepo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        _productRepo.Setup(r => r.TryReserveStockAsync(product.Id, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = async () => await Svc().CreateAsync(dto, "user-1");
        await act.Should().ThrowAsync<DomainException>().WithMessage("*Insufficient stock*");
        _productRepo.Verify(r => r.RestoreStockAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_SecondLineReservationFails_RestoresEarlierLines()
    {
        var first = MakeProduct();
        var second = MakeProduct();
        var dto = new CreateOrderDto([
            new OrderItemDto(first.Id, 1),
            new OrderItemDto(second.Id, 1)
        ]);
        _productRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
            .Returns((Guid id) => Task.FromResult<Product?>(id == first.Id ? first : second));
        // Reservations run in ProductId order; whichever line is reserved first
        // succeeds and the second fails, forcing a rollback of the first.
        var reserveCalls = 0;
        _productRepo.Setup(r => r.TryReserveStockAsync(It.IsAny<Guid>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref reserveCalls) == 1);

        var act = async () => await Svc().CreateAsync(dto, "user-1");

        await act.Should().ThrowAsync<DomainException>().WithMessage("*Insufficient stock*");
        _productRepo.Verify(r => r.RestoreStockAsync(It.IsAny<Guid>(), 1, It.IsAny<CancellationToken>()), Times.Once);
        _payment.Verify(p => p.CreatePaymentIntentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_PaymentSetupFails_RestoresReservedStock()
    {
        var product = MakeProduct();
        _productRepo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        _productRepo.Setup(r => r.TryReserveStockAsync(product.Id, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _payment.Setup(p => p.CreatePaymentIntentAsync(It.IsAny<decimal>(), "usd", It.IsAny<Guid>()))
            .ThrowsAsync(new InvalidOperationException("Stripe unavailable"));

        var act = () => Svc().CreateAsync(
            new CreateOrderDto([new OrderItemDto(product.Id, 2)]),
            "user-1");

        await act.Should().ThrowAsync<InvalidOperationException>();
        _productRepo.Verify(r => r.RestoreStockAsync(product.Id, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_OrderPersistenceFails_CancelsIntentAndRestoresStock()
    {
        var product = MakeProduct();
        _productRepo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        _productRepo.Setup(r => r.TryReserveStockAsync(product.Id, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _payment.Setup(p => p.CreatePaymentIntentAsync(It.IsAny<decimal>(), "usd", It.IsAny<Guid>()))
            .ReturnsAsync(new PaymentIntentDetails("pi_test_123", "pi_test_123_secret"));
        _orderRepo.Setup(r => r.CreateAsync(It.IsAny<Order>()))
            .ThrowsAsync(new InvalidOperationException("Database unavailable"));

        var act = () => Svc().CreateAsync(
            new CreateOrderDto([new OrderItemDto(product.Id, 2)]),
            "user-1");

        await act.Should().ThrowAsync<InvalidOperationException>();
        _productRepo.Verify(r => r.RestoreStockAsync(product.Id, 2, It.IsAny<CancellationToken>()), Times.Once);
        _payment.Verify(p => p.CancelPaymentIntentAsync("pi_test_123"), Times.Once);
    }

    // ─── GetByIdAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_OrderExists_AdminAccess_ReturnsDto()
    {
        var order = MakeOrder("user-1");
        _orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);

        var result = await Svc().GetByIdAsync(order.Id, null); // null = admin

        result.Id.Should().Be(order.Id);
    }

    [Fact]
    public async Task GetByIdAsync_OrderExists_OwnerAccess_ReturnsDto()
    {
        var order = MakeOrder("user-1");
        _orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);

        var result = await Svc().GetByIdAsync(order.Id, "user-1");

        result.Id.Should().Be(order.Id);
    }

    [Fact]
    public async Task GetByIdAsync_OrderExists_WrongUser_ThrowsUnauthorized()
    {
        var order = MakeOrder("user-1");
        _orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);

        var act = async () => await Svc().GetByIdAsync(order.Id, "user-2");
        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("Access denied");
    }

    [Fact]
    public async Task GetByIdAsync_OrderNotFound_ThrowsKeyNotFound()
    {
        var id = Guid.NewGuid();
        _orderRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Order?)null);

        var act = async () => await Svc().GetByIdAsync(id, "user-1");
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage($"*{id}*");
    }

    // ─── UpdateOrderStatusAsync ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateOrderStatusAsync_OrderExists_UpdatesStatus()
    {
        var order = MakeOrder();
        order.SetPaymentIntent("pi_test_123");
        order.MarkAsPaid();
        _orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);

        await Svc().UpdateOrderStatusAsync(order.Id, (int)OrderStatus.Shipped);

        order.Status.Should().Be(OrderStatus.Shipped);
        _orderRepo.Verify(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateOrderStatusAsync_OrderNotFound_ThrowsKeyNotFound()
    {
        var id = Guid.NewGuid();
        _orderRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Order?)null);

        var act = async () => await Svc().UpdateOrderStatusAsync(id, (int)OrderStatus.Paid);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateOrderStatusAsync_CannotMarkOrderPaidWithoutVerifiedPayment()
    {
        var order = MakeOrder();
        order.SetPaymentIntent("pi_test_123");
        _orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);

        var act = () => Svc().UpdateOrderStatusAsync(order.Id, (int)OrderStatus.Paid);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*verified payment*");
        _orderRepo.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── GetMyOrdersAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyOrdersAsync_ReturnsMappedDtos()
    {
        var order = MakeOrder("user-42");
        _orderRepo.Setup(r => r.GetByUserIdAsync("user-42")).ReturnsAsync([order]);

        var result = await Svc().GetMyOrdersAsync("user-42");

        result.Should().HaveCount(1);
        result.First().UserId.Should().Be("user-42");
    }

    [Fact]
    public async Task GetMyOrdersAsync_NoOrders_ReturnsEmpty()
    {
        _orderRepo.Setup(r => r.GetByUserIdAsync("user-x")).ReturnsAsync([]);

        var result = await Svc().GetMyOrdersAsync("user-x");

        result.Should().BeEmpty();
    }

    // ─── GetAllOrdersAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllOrdersAsync_ReturnsAllOrders()
    {
        var orders = new List<Order> { MakeOrder("a"), MakeOrder("b") };
        _orderRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(orders);

        var result = await Svc().GetAllOrdersAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_MatchingVerifiedPayment_MarksOrderPaid()
    {
        var order = MakeOrder();
        order.SetPaymentIntent("pi_test_123");
        _orderRepo.Setup(r => r.GetByPaymentIntentIdAsync("pi_test_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _orderRepo.Setup(r => r.TryTransitionStatusAsync(
                order.Id, OrderStatus.PaymentProcessing, OrderStatus.Paid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Svc().ConfirmPaymentAsync("pi_test_123", 999, 999, "usd");

        result.Should().BeTrue();
        _orderRepo.Verify(r => r.TryTransitionStatusAsync(
            order.Id, OrderStatus.PaymentProcessing, OrderStatus.Paid, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(998, "usd")]
    [InlineData(999, "cad")]
    public async Task ConfirmPaymentAsync_AmountOrCurrencyMismatch_DoesNotMarkPaid(long amount, string currency)
    {
        var order = MakeOrder();
        order.SetPaymentIntent("pi_test_123");
        _orderRepo.Setup(r => r.GetByPaymentIntentIdAsync("pi_test_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var result = await Svc().ConfirmPaymentAsync("pi_test_123", amount, amount, currency);

        result.Should().BeFalse();
        order.Status.Should().Be(OrderStatus.PaymentProcessing);
        _orderRepo.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
        _orderRepo.Verify(r => r.TryTransitionStatusAsync(
            It.IsAny<Guid>(), It.IsAny<OrderStatus>(), It.IsAny<OrderStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_AlreadyPaidOrder_IsIdempotent()
    {
        var order = MakeOrder();
        order.SetPaymentIntent("pi_test_123");
        order.MarkAsPaid();
        _orderRepo.Setup(r => r.GetByPaymentIntentIdAsync("pi_test_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var result = await Svc().ConfirmPaymentAsync("pi_test_123", 999, 999, "usd");

        result.Should().BeTrue();
        _orderRepo.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_UnknownIntent_DoesNotUpdateOrder()
    {
        _orderRepo.Setup(r => r.GetByPaymentIntentIdAsync("pi_missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var result = await Svc().ConfirmPaymentAsync("pi_missing", 999, 999, "usd");

        result.Should().BeFalse();
        _orderRepo.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── ProcessStripeEventAsync ──────────────────────────────────────────────

    private void SetupTransactionPassthrough() =>
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<Task<bool>> work, CancellationToken ct) => work());

    private void SetupEventClaim(string eventId, bool firstDelivery) =>
        _stripeEvents.Setup(r => r.TryRecordProcessedEventAsync(
                eventId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstDelivery);

    [Fact]
    public async Task ProcessStripeEventAsync_DuplicateEvent_DoesNoWork()
    {
        SetupTransactionPassthrough();
        SetupEventClaim("evt_dup", firstDelivery: false);

        await Svc().ProcessStripeEventAsync("evt_dup", "payment_intent.canceled", "pi_x", 0, 0, "usd", null);

        _orderRepo.Verify(r => r.TryTransitionStatusAsync(
            It.IsAny<Guid>(), It.IsAny<OrderStatus>(), It.IsAny<OrderStatus>(), It.IsAny<CancellationToken>()), Times.Never);
        _productRepo.Verify(r => r.RestoreStockAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessStripeEventAsync_PaymentFailed_RecordsFailureAndKeepsReservation()
    {
        SetupTransactionPassthrough();
        SetupEventClaim("evt_fail", firstDelivery: true);
        var order = MakeOrder();
        order.SetPaymentIntent("pi_fail");
        _orderRepo.Setup(r => r.GetByPaymentIntentIdAsync("pi_fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        await Svc().ProcessStripeEventAsync("evt_fail", "payment_intent.payment_failed", "pi_fail", 0, 0, "usd", "card_declined");

        order.LastPaymentFailureCode.Should().Be("card_declined");
        order.LastPaymentFailureAt.Should().NotBeNull();
        order.Status.Should().Be(OrderStatus.PaymentProcessing);
        _orderRepo.Verify(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
        _orderRepo.Verify(r => r.TryTransitionStatusAsync(
            It.IsAny<Guid>(), It.IsAny<OrderStatus>(), It.IsAny<OrderStatus>(), It.IsAny<CancellationToken>()), Times.Never);
        _productRepo.Verify(r => r.RestoreStockAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessStripeEventAsync_CanceledPaidOrder_IsIgnored()
    {
        SetupTransactionPassthrough();
        SetupEventClaim("evt_c_paid", firstDelivery: true);
        var order = MakeOrder();
        order.SetPaymentIntent("pi_paid");
        order.MarkAsPaid();
        _orderRepo.Setup(r => r.GetByPaymentIntentIdAsync("pi_paid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        await Svc().ProcessStripeEventAsync("evt_c_paid", "payment_intent.canceled", "pi_paid", 0, 0, "usd", null);

        _orderRepo.Verify(r => r.TryTransitionStatusAsync(
            It.IsAny<Guid>(), It.IsAny<OrderStatus>(), It.IsAny<OrderStatus>(), It.IsAny<CancellationToken>()), Times.Never);
        _productRepo.Verify(r => r.RestoreStockAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessStripeEventAsync_CanceledProcessingOrder_RestoresStockOnce()
    {
        SetupTransactionPassthrough();
        SetupEventClaim("evt_c", firstDelivery: true);
        var order = MakeOrder();
        order.SetPaymentIntent("pi_cancel");
        _orderRepo.Setup(r => r.GetByPaymentIntentIdAsync("pi_cancel", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _orderRepo.Setup(r => r.TryTransitionStatusAsync(
                order.Id, OrderStatus.PaymentProcessing, OrderStatus.Cancelled, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Svc().ProcessStripeEventAsync("evt_c", "payment_intent.canceled", "pi_cancel", 0, 0, "usd", null);

        var item = order.Items.Single();
        _productRepo.Verify(r => r.RestoreStockAsync(item.ProductId, item.Quantity, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessStripeEventAsync_SucceededForCancelledOrder_RefundsLatePayment()
    {
        SetupTransactionPassthrough();
        SetupEventClaim("evt_late", firstDelivery: true);
        var order = MakeOrder();
        order.SetPaymentIntent("pi_late");
        _orderRepo.Setup(r => r.GetByPaymentIntentIdAsync("pi_late", It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        // Simulate the cancel having already won the race: the atomic
        // PaymentProcessing→Paid transition affects 0 rows and the committed
        // status reads back as Cancelled.
        _orderRepo.Setup(r => r.TryTransitionStatusAsync(
                order.Id, OrderStatus.PaymentProcessing, OrderStatus.Paid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _orderRepo.Setup(r => r.GetStatusByPaymentIntentIdAsync("pi_late", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrderStatus.Cancelled);
        _payment.Setup(p => p.RefundPaymentIntentAsync("pi_late", $"secure-shop-refund-{order.Id}"))
            .ReturnsAsync("re_late_123");

        await Svc().ProcessStripeEventAsync("evt_late", "payment_intent.succeeded", "pi_late", 999, 999, "usd", null);

        order.StripeRefundId.Should().Be("re_late_123");
        order.Status.Should().Be(OrderStatus.PaymentProcessing); // never marked Paid
        _payment.Verify(p => p.RefundPaymentIntentAsync("pi_late", $"secure-shop-refund-{order.Id}"), Times.Once);
    }
}
