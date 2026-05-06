using FluentAssertions;
using Moq;
using SecureShop.Application.DTOs.Orders;
using SecureShop.Application.Interfaces;
using SecureShop.Application.Services;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Enums;
using SecureShop.Domain.Exceptions;

namespace SecureShop.UnitTests;

public class OrderServiceTests
{
    private readonly Mock<IOrderRepository> _orderRepo = new();
    private readonly Mock<IProductRepository> _productRepo = new();
    private readonly Mock<IPaymentService> _payment = new();
    private OrderService Svc() => new(_orderRepo.Object, _productRepo.Object, _payment.Object);

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
        _payment.Setup(p => p.CreatePaymentIntentAsync(It.IsAny<decimal>(), "usd", It.IsAny<Guid>()))
                .ReturnsAsync("pi_test_secret");
        _orderRepo.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync((Order o) => o);

        var result = await Svc().CreateAsync(dto, "user-1");

        result.Should().NotBeNull();
        result.UserId.Should().Be("user-1");
        result.TotalAmount.Should().Be(9.99m * 2);
        result.StripePaymentIntentId.Should().Be("pi_test_secret");
        _productRepo.Verify(r => r.UpdateAsync(It.IsAny<Product>()), Times.Once);
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

        var act = async () => await Svc().CreateAsync(dto, "user-1");
        await act.Should().ThrowAsync<DomainException>().WithMessage("*Insufficient stock*");
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
        _orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);

        await Svc().UpdateOrderStatusAsync(order.Id, (int)OrderStatus.Shipped);

        order.Status.Should().Be(OrderStatus.Shipped);
        _orderRepo.Verify(r => r.UpdateAsync(order), Times.Once);
    }

    [Fact]
    public async Task UpdateOrderStatusAsync_OrderNotFound_ThrowsKeyNotFound()
    {
        var id = Guid.NewGuid();
        _orderRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Order?)null);

        var act = async () => await Svc().UpdateOrderStatusAsync(id, (int)OrderStatus.Paid);
        await act.Should().ThrowAsync<KeyNotFoundException>();
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
}
