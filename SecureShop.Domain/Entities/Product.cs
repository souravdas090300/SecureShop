using SecureShop.Domain.Exceptions;

namespace SecureShop.Domain.Entities;

/// <summary>
/// Aggregate root representing a product listed in the store.
/// All mutations go through domain methods so that invariants
/// (e.g. price > 0, stock >= 0) are always enforced.
/// </summary>
public class Product
{
    /// <summary>Unique identifier for the product.</summary>
    public Guid Id { get; private set; }

    /// <summary>Display name of the product.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Long-form description shown on the product detail page.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Retail price in the store's base currency.</summary>
    public decimal Price { get; private set; }

    /// <summary>Number of units currently available to purchase.</summary>
    public int StockQuantity { get; private set; }

    /// <summary>Broad grouping used for filtering (e.g. "Electronics").</summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>Optional URL of the product's primary image.</summary>
    public string? ImageUrl { get; private set; }

    /// <summary>
    /// Indicates whether the product is visible and purchasable.
    /// Soft-deleted products have <c>IsActive = false</c>.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>UTC timestamp when the product record was first created.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>UTC timestamp of the most recent update.</summary>
    public DateTime UpdatedAt { get; private set; }

    // EF Core requires a parameterless constructor for materialisation.
    private Product() { }

    /// <summary>
    /// Factory method — creates a new active <see cref="Product"/> after validating all invariants.
    /// </summary>
    /// <param name="name">Non-empty display name.</param>
    /// <param name="description">Product description (may be empty).</param>
    /// <param name="price">Must be greater than zero.</param>
    /// <param name="stock">Must be zero or more.</param>
    /// <param name="category">Category name used for filtering.</param>
    /// <param name="imageUrl">Optional URL pointing to the product image.</param>
    /// <returns>A new, active <see cref="Product"/>.</returns>
    /// <exception cref="DomainException">Thrown when any invariant is violated.</exception>
    public static Product Create(string name, string description,
        decimal price, int stock, string category, string? imageUrl = null)
    {
        // Validate all business rules before constructing the object.
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Product name is required");
        if (price <= 0)
            throw new DomainException("Price must be greater than zero");
        if (stock < 0)
            throw new DomainException("Stock cannot be negative");

        return new Product
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description.Trim(),
            Price = price,
            StockQuantity = stock,
            Category = category.Trim(),
            ImageUrl = imageUrl,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Updates mutable fields. Enforces the same price/stock invariants as <see cref="Create"/>.
    /// </summary>
    /// <param name="name">New display name.</param>
    /// <param name="description">New description.</param>
    /// <param name="price">New price (must be &gt; 0).</param>
    /// <param name="category">New category.</param>
    /// <param name="stockQuantity">New stock level (must be &gt;= 0).</param>
    /// <param name="imageUrl">New image URL (nullable).</param>
    /// <exception cref="DomainException">Thrown when price or stock violates invariants.</exception>
    public void Update(string name, string description, decimal price, string category, int stockQuantity, string? imageUrl)
    {
        if (price <= 0) throw new DomainException("Price must be greater than zero");
        if (stockQuantity < 0) throw new DomainException("Stock cannot be negative");
        Name = name.Trim();
        Description = description.Trim();
        Price = price;
        Category = category.Trim();
        StockQuantity = stockQuantity;
        ImageUrl = imageUrl;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Decrements stock by <paramref name="quantity"/> units.
    /// Called during order creation to reserve inventory.
    /// </summary>
    /// <param name="quantity">Number of units to deduct.</param>
    /// <exception cref="DomainException">Thrown when requested quantity exceeds available stock.</exception>
    public void ReduceStock(int quantity)
    {
        if (quantity > StockQuantity)
            throw new DomainException($"Insufficient stock. Available: {StockQuantity}");
        StockQuantity -= quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Soft-deletes the product by setting <see cref="IsActive"/> to <c>false</c>.
    /// The record is retained in the database for order history references.
    /// </summary>
    public void Deactivate() { IsActive = false; UpdatedAt = DateTime.UtcNow; }
}