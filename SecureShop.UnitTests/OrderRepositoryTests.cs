using FluentAssertions;
using SecureShop.Domain.Entities;
using SecureShop.Infrastructure;

namespace SecureShop.UnitTests;

/// <summary>
/// Integration tests for OrderRepository using the EF InMemory database.
/// The Order→User relationship is required; EF InMemory filters out orders whose
/// User FK is unresolvable when executing LINQ queries with Include(o => o.User).
/// We therefore seed ApplicationUser rows for any userId we create orders against.
/// </summary>
public class OrderRepositoryTests : EfTestBase
{
    private OrderRepository Repo() => new(Db);

    private static Order MakeOrder(string userId = "user-1")
    {
        var item = OrderItem.Create(Guid.NewGuid(), 2, 9.99m);
        return Order.Create(userId, new List<OrderItem> { item });
    }

    /// <summary>Seeds a minimal ApplicationUser so the required Order→User FK resolves.</summary>
    private async Task SeedUserAsync(string userId)
    {
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = userId,
            NormalizedUserName = userId.ToUpperInvariant(),
            Email = $"{userId}@test.com",
            NormalizedEmail = $"{userId}@test.com".ToUpperInvariant(),
            FirstName = "Test",
            LastName = "User",
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
    }

    // ─── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PersistsOrder()
    {
        var order = MakeOrder();

        await Repo().CreateAsync(order);

        Db.Orders.Should().ContainSingle(o => o.Id == order.Id);
    }

    [Fact]
    public async Task CreateAsync_ReturnsCreatedOrder()
    {
        var order = MakeOrder();

        var result = await Repo().CreateAsync(order);

        result.Id.Should().Be(order.Id);
    }

    // ─── GetByIdAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsOrder()
    {
        await SeedUserAsync("user-1");
        var order = MakeOrder("user-1");
        Db.Orders.Add(order);
        await Db.SaveChangesAsync();

        var result = await Repo().GetByIdAsync(order.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(order.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_IncludesItems()
    {
        await SeedUserAsync("user-1");
        var order = MakeOrder("user-1");
        Db.Orders.Add(order);
        await Db.SaveChangesAsync();

        var result = await Repo().GetByIdAsync(order.Id);

        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var result = await Repo().GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    // ─── GetByUserIdAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByUserIdAsync_ReturnsOrdersForMatchingUser()
    {
        await SeedUserAsync("alice");
        await SeedUserAsync("bob");
        Db.Orders.AddRange(MakeOrder("alice"), MakeOrder("alice"), MakeOrder("bob"));
        await Db.SaveChangesAsync();

        var result = await Repo().GetByUserIdAsync("alice");

        result.Should().HaveCount(2);
        result.Should().OnlyContain(o => o.UserId == "alice");
    }

    [Fact]
    public async Task GetByUserIdAsync_NoMatchingUser_ReturnsEmpty()
    {
        Db.Orders.Add(MakeOrder("alice"));
        await Db.SaveChangesAsync();

        var result = await Repo().GetByUserIdAsync("nobody");

        result.Should().BeEmpty();
    }

    // ─── GetAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsAllOrders()
    {
        await SeedUserAsync("alice");
        await SeedUserAsync("bob");
        await SeedUserAsync("carol");
        Db.Orders.AddRange(MakeOrder("alice"), MakeOrder("bob"), MakeOrder("carol"));
        await Db.SaveChangesAsync();

        var result = await Repo().GetAllAsync();

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetAllAsync_EmptyDb_ReturnsEmpty()
    {
        var result = await Repo().GetAllAsync();
        result.Should().BeEmpty();
    }

    // ─── UpdateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_PersistsStatusChange()
    {
        var order = MakeOrder();
        Db.Orders.Add(order);
        await Db.SaveChangesAsync();

        order.MarkAsPaid();
        await Repo().UpdateAsync(order);

        var saved = await Db.Orders.FindAsync(order.Id);
        saved!.Status.Should().Be(SecureShop.Domain.Enums.OrderStatus.Paid);
    }

    [Fact]
    public async Task UpdateAsync_PersistsPaymentIntentChange()
    {
        var order = MakeOrder();
        Db.Orders.Add(order);
        await Db.SaveChangesAsync();

        order.SetPaymentIntent("pi_test_123");
        await Repo().UpdateAsync(order);

        var saved = await Db.Orders.FindAsync(order.Id);
        saved!.StripePaymentIntentId.Should().Be("pi_test_123");
    }
}
