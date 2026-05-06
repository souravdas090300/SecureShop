using FluentAssertions;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Exceptions;

namespace SecureShop.UnitTests;

public class ProductEntityTests
{
    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidData_SetsAllProperties()
    {
        var before = DateTime.UtcNow;
        var product = Product.Create("Widget", "A fine widget", 9.99m, 5, "Home", "http://img.url");

        product.Id.Should().NotBeEmpty();
        product.Name.Should().Be("Widget");
        product.Description.Should().Be("A fine widget");
        product.Price.Should().Be(9.99m);
        product.StockQuantity.Should().Be(5);
        product.Category.Should().Be("Home");
        product.ImageUrl.Should().Be("http://img.url");
        product.IsActive.Should().BeTrue();
        product.CreatedAt.Should().BeOnOrAfter(before);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_ThrowsDomainException(string name)
    {
        var act = () => Product.Create(name, "desc", 1m, 0, "Cat");
        act.Should().Throw<DomainException>().WithMessage("Product name is required");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void Create_WithNonPositivePrice_ThrowsDomainException(decimal price)
    {
        var act = () => Product.Create("Name", "desc", price, 0, "Cat");
        act.Should().Throw<DomainException>().WithMessage("Price must be greater than zero");
    }

    [Fact]
    public void Create_WithNegativeStock_ThrowsDomainException()
    {
        var act = () => Product.Create("Name", "desc", 1m, -1, "Cat");
        act.Should().Throw<DomainException>().WithMessage("Stock cannot be negative");
    }

    [Fact]
    public void Create_TrimsWhitespace()
    {
        var product = Product.Create("  Widget  ", "  desc  ", 1m, 0, "  Cat  ");
        product.Name.Should().Be("Widget");
        product.Description.Should().Be("desc");
        product.Category.Should().Be("Cat");
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_WithValidData_ChangesFields()
    {
        var product = Product.Create("Old", "Old desc", 5m, 3, "Electronics");

        product.Update("New", "New desc", 10m, "Books", 7, null);

        product.Name.Should().Be("New");
        product.Description.Should().Be("New desc");
        product.Price.Should().Be(10m);
        product.Category.Should().Be("Books");
        product.StockQuantity.Should().Be(7);
        product.ImageUrl.Should().BeNull();
    }

    [Fact]
    public void Update_WithNegativePrice_ThrowsDomainException()
    {
        var product = Product.Create("Name", "desc", 5m, 0, "Cat");
        var act = () => product.Update("Name", "desc", -1m, "Cat", 0, null);
        act.Should().Throw<DomainException>().WithMessage("Price must be greater than zero");
    }

    [Fact]
    public void Update_WithNegativeStock_ThrowsDomainException()
    {
        var product = Product.Create("Name", "desc", 5m, 0, "Cat");
        var act = () => product.Update("Name", "desc", 5m, "Cat", -5, null);
        act.Should().Throw<DomainException>().WithMessage("Stock cannot be negative");
    }

    // ─── ReduceStock ──────────────────────────────────────────────────────────

    [Fact]
    public void ReduceStock_ExactQuantity_SetsStockToZero()
    {
        var product = Product.Create("Name", "desc", 1m, 10, "Cat");
        product.ReduceStock(10);
        product.StockQuantity.Should().Be(0);
    }

    [Fact]
    public void ReduceStock_ByOne_DecrementsStock()
    {
        var product = Product.Create("Name", "desc", 1m, 10, "Cat");
        product.ReduceStock(1);
        product.StockQuantity.Should().Be(9);
    }

    [Fact]
    public void ReduceStock_MoreThanAvailable_ThrowsDomainException()
    {
        var product = Product.Create("Name", "desc", 1m, 3, "Cat");
        var act = () => product.ReduceStock(4);
        act.Should().Throw<DomainException>().WithMessage("*Insufficient stock*");
    }

    // ─── Deactivate ───────────────────────────────────────────────────────────

    [Fact]
    public void Deactivate_SetsIsActiveToFalse()
    {
        var product = Product.Create("Name", "desc", 1m, 0, "Cat");
        product.IsActive.Should().BeTrue();

        product.Deactivate();

        product.IsActive.Should().BeFalse();
    }
}
