using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SecureShop.Domain.Entities;
using SecureShop.Infrastructure;

namespace SecureShop.UnitTests;

/// <summary>
/// Creates a fresh in-memory AppDbContext for each test.
/// Note: AppDbContext inherits IdentityDbContext so we use UseInMemoryDatabase.
/// The Product entity has a global query filter for IsActive — tests that need
/// inactive products must use IgnoreQueryFilters().
/// </summary>
public abstract class EfTestBase : IDisposable
{
    protected readonly AppDbContext Db;

    protected EfTestBase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // unique DB per test
            .Options;
        Db = new AppDbContext(options);
    }

    public void Dispose() => Db.Dispose();
}

public class ProductRepositoryTests : EfTestBase
{
    private ProductRepository Repo() => new(Db);

    private static Product Active(string name = "Widget", string cat = "Electronics", string? desc = null)
        => Product.Create(name, desc ?? "desc", 9.99m, 10, cat);

    // ─── GetAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyActiveProducts()
    {
        var active = Active();
        var inactive = Active("Inactive");
        inactive.Deactivate();
        Db.Products.AddRange(active, inactive);
        await Db.SaveChangesAsync();

        var result = await Repo().GetAllAsync(null, null, 1, 10);

        result.Should().ContainSingle(p => p.Name == "Widget");
    }

    [Fact]
    public async Task GetAllAsync_FiltersByCategory()
    {
        Db.Products.AddRange(Active("A", "Electronics"), Active("B", "Books"));
        await Db.SaveChangesAsync();

        var result = await Repo().GetAllAsync("Books", null, 1, 10);

        result.Should().ContainSingle().Which.Name.Should().Be("B");
    }

    [Fact]
    public async Task GetAllAsync_FiltersBySearch_MatchesName()
    {
        Db.Products.AddRange(
            Active("Blue Widget", "Electronics"),
            Active("Red Gadget", "Electronics"));
        await Db.SaveChangesAsync();

        var result = await Repo().GetAllAsync(null, "Blue", 1, 10);

        result.Should().ContainSingle().Which.Name.Should().Be("Blue Widget");
    }

    [Fact]
    public async Task GetAllAsync_FiltersBySearch_MatchesDescription()
    {
        Db.Products.Add(Active("Widget", "Electronics", "special blue widget"));
        Db.Products.Add(Active("Gadget", "Electronics", "ordinary red gadget"));
        await Db.SaveChangesAsync();

        var result = await Repo().GetAllAsync(null, "blue", 1, 10);

        result.Should().ContainSingle().Which.Name.Should().Be("Widget");
    }

    [Fact]
    public async Task GetAllAsync_PaginatesCorrectly()
    {
        for (int i = 1; i <= 5; i++)
            Db.Products.Add(Active($"Product {i}"));
        await Db.SaveChangesAsync();

        var page1 = await Repo().GetAllAsync(null, null, 1, 3);
        var page2 = await Repo().GetAllAsync(null, null, 2, 3);

        page1.Should().HaveCount(3);
        page2.Should().HaveCount(2);
    }

    // ─── GetByIdAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsProduct()
    {
        var product = Active();
        Db.Products.Add(product);
        await Db.SaveChangesAsync();

        var result = await Repo().GetByIdAsync(product.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(product.Id);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var result = await Repo().GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    // ─── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PersistsProduct()
    {
        var product = Active();

        await Repo().CreateAsync(product);

        var saved = await Db.Products.FindAsync(product.Id);
        saved.Should().NotBeNull();
        saved!.Name.Should().Be("Widget");
    }

    // ─── UpdateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var product = Active();
        Db.Products.Add(product);
        await Db.SaveChangesAsync();

        product.Update("Updated", "new desc", 19.99m, "Books", 5, null);
        await Repo().UpdateAsync(product);

        var saved = await Db.Products.FindAsync(product.Id);
        saved!.Name.Should().Be("Updated");
        saved.Price.Should().Be(19.99m);
    }

    // ─── ExistsAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_ExistingId_ReturnsTrue()
    {
        var product = Active();
        Db.Products.Add(product);
        await Db.SaveChangesAsync();

        (await Repo().ExistsAsync(product.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_UnknownId_ReturnsFalse()
    {
        (await Repo().ExistsAsync(Guid.NewGuid())).Should().BeFalse();
    }

    // ─── CountAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CountAsync_ReturnsOnlyActiveCount()
    {
        var active = Active();
        var inactive = Active("Inactive");
        inactive.Deactivate();
        Db.Products.AddRange(active, inactive);
        await Db.SaveChangesAsync();

        var count = await Repo().CountAsync(null, null);

        count.Should().Be(1);
    }

    [Fact]
    public async Task CountAsync_WithCategoryFilter_ReturnsMatchingCount()
    {
        Db.Products.AddRange(Active("A", "Electronics"), Active("B", "Books"), Active("C", "Books"));
        await Db.SaveChangesAsync();

        var count = await Repo().CountAsync("Books", null);

        count.Should().Be(2);
    }

    [Fact]
    public async Task CountAsync_WithSearchFilter_ReturnsMatchingCount()
    {
        Db.Products.AddRange(Active("Blue Widget"), Active("Red Widget"), Active("Gadget"));
        await Db.SaveChangesAsync();

        var count = await Repo().CountAsync(null, "Widget");

        count.Should().Be(2);
    }
}
