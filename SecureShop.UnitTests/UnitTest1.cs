using SecureShop.Domain.Entities;
using SecureShop.Domain.Enums;
using SecureShop.Domain.Exceptions;

namespace SecureShop.UnitTests;

public class ProductTests
{
    [Fact]
    public void Create_ValidProduct_ShouldSucceed()
    {
        // Arrange & Act
        var product = Product.Create("Test Product", "Description", 99.99m, 10, "Electronics");

        // Assert
        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.Equal("Test Product", product.Name);
        Assert.Equal(99.99m, product.Price);
        Assert.Equal(10, product.StockQuantity);
        Assert.True(product.IsActive);
    }

    [Fact]
    public void Create_EmptyName_ShouldThrowDomainException()
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() =>
            Product.Create("", "Description", 99.99m, 10, "Electronics"));
        Assert.Equal("Product name is required", ex.Message);
    }

    [Fact]
    public void Create_NegativePrice_ShouldThrowDomainException()
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() =>
            Product.Create("Test", "Description", -10m, 10, "Electronics"));
        Assert.Equal("Price must be greater than zero", ex.Message);
    }

    [Fact]
    public void ReduceStock_SufficientQuantity_ShouldSucceed()
    {
        // Arrange
        var product = Product.Create("Test", "Description", 99.99m, 10, "Electronics");

        // Act
        product.ReduceStock(5);

        // Assert
        Assert.Equal(5, product.StockQuantity);
    }

    [Fact]
    public void ReduceStock_InsufficientQuantity_ShouldThrowDomainException()
    {
        // Arrange
        var product = Product.Create("Test", "Description", 99.99m, 10, "Electronics");

        // Act & Assert
        var ex = Assert.Throws<DomainException>(() => product.ReduceStock(15));
        Assert.StartsWith("Insufficient stock", ex.Message);
    }
}

public class OrderTests
{
    [Fact]
    public void Create_ValidOrder_ShouldSucceed()
    {
        // Arrange
        var items = new List<OrderItem>
        {
            OrderItem.Create(Guid.NewGuid(), 2, 50m)
        };

        // Act
        var order = Order.Create("user123", items);

        // Assert
        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Equal("user123", order.UserId);
        Assert.Equal(100m, order.TotalAmount);
        Assert.Equal(SecureShop.Domain.Enums.OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void Create_EmptyItems_ShouldThrowDomainException()
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() =>
            Order.Create("user123", new List<OrderItem>()));
        Assert.Equal("Order must have at least one item", ex.Message);
    }
}
