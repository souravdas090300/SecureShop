using FluentAssertions;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Enums;
using SecureShop.Domain.Exceptions;

namespace SecureShop.UnitTests;

public class OrderEntityTests
{
    private static List<OrderItem> TwoItems() =>
    [
        OrderItem.Create(Guid.NewGuid(), 2, 10m),
        OrderItem.Create(Guid.NewGuid(), 1, 5m)
    ];

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidItems_SetsPendingStatusAndCalculatesTotal()
    {
        var order = Order.Create("user-1", TwoItems());

        order.Id.Should().NotBeEmpty();
        order.UserId.Should().Be("user-1");
        order.Status.Should().Be(OrderStatus.Pending);
        order.TotalAmount.Should().Be(25m); // 2*10 + 1*5
        order.Items.Should().HaveCount(2);
    }

    [Fact]
    public void Create_WithNoItems_ThrowsDomainException()
    {
        var act = () => Order.Create("user-1", []);
        act.Should().Throw<DomainException>().WithMessage("Order must have at least one item");
    }

    // ─── SetPaymentIntent ─────────────────────────────────────────────────────

    [Fact]
    public void SetPaymentIntent_SetsIdAndChangesStatusToPaymentProcessing()
    {
        var order = Order.Create("user-1", TwoItems());

        order.SetPaymentIntent("pi_123");

        order.StripePaymentIntentId.Should().Be("pi_123");
        order.Status.Should().Be(OrderStatus.PaymentProcessing);
    }

    // ─── MarkAsPaid ───────────────────────────────────────────────────────────

    [Fact]
    public void MarkAsPaid_ChangesStatusToPaid()
    {
        var order = Order.Create("user-1", TwoItems());
        order.MarkAsPaid();
        order.Status.Should().Be(OrderStatus.Paid);
    }

    // ─── Cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_FromPendingStatus_SetsStatusToCancelled()
    {
        var order = Order.Create("user-1", TwoItems());
        order.Cancel();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_FromShippedStatus_ThrowsDomainException()
    {
        var order = Order.Create("user-1", TwoItems());
        order.SetStatus(OrderStatus.Shipped);

        var act = () => order.Cancel();
        act.Should().Throw<DomainException>().WithMessage("Cannot cancel a shipped or delivered order");
    }

    [Fact]
    public void Cancel_FromDeliveredStatus_ThrowsDomainException()
    {
        var order = Order.Create("user-1", TwoItems());
        order.SetStatus(OrderStatus.Delivered);

        var act = () => order.Cancel();
        act.Should().Throw<DomainException>().WithMessage("Cannot cancel a shipped or delivered order");
    }

    // ─── SetStatus ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public void SetStatus_AllowsAnyStatus(OrderStatus status)
    {
        var order = Order.Create("user-1", TwoItems());
        order.SetStatus(status);
        order.Status.Should().Be(status);
    }
}

public class OrderItemTests
{
    [Fact]
    public void Create_SetsAllProperties()
    {
        var productId = Guid.NewGuid();
        var item = OrderItem.Create(productId, 3, 15.50m);

        item.Id.Should().NotBeEmpty();
        item.ProductId.Should().Be(productId);
        item.Quantity.Should().Be(3);
        item.UnitPrice.Should().Be(15.50m);
    }
}
