namespace SecureShop.Domain.Entities;

/// <summary>
/// Represents a single line item within an <see cref="Order"/>,
/// capturing the purchased product, quantity, and the unit price
/// at the time of purchase.
/// </summary>
public class OrderItem
{
    /// <summary>Unique identifier for this order item.</summary>
    public Guid Id { get; private set; }

    /// <summary>Foreign key referencing the parent <see cref="Order"/>.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Foreign key referencing the <see cref="Product"/> that was ordered.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Navigation property to the associated product (may be null if not loaded).</summary>
    public Product? Product { get; private set; }

    /// <summary>Number of units ordered.</summary>
    public int Quantity { get; private set; }

    /// <summary>Price per unit captured at the moment the order was created.</summary>
    public decimal UnitPrice { get; private set; }

    // EF Core requires a parameterless constructor for materialisation.
    private OrderItem() { }

    /// <summary>
    /// Creates a new <see cref="OrderItem"/> for the given product.
    /// </summary>
    /// <param name="productId">ID of the product being ordered.</param>
    /// <param name="quantity">Number of units.</param>
    /// <param name="unitPrice">Price per unit at the time of order.</param>
    /// <returns>A new, fully initialised <see cref="OrderItem"/>.</returns>
    public static OrderItem Create(Guid productId, int quantity, decimal unitPrice)
    {
        return new OrderItem
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Quantity = quantity,
            UnitPrice = unitPrice
        };
    }
}