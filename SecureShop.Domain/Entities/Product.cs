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

    /// <summary>Optional full specifications / technical details.</summary>
    public string? Details { get; set; }

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
    public static Product Create(
        string name,
        string description,
        decimal price,
        int stock,
        string category,
        string? imageUrl = null,
        string? details = null)
    {
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
            Details = details,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Updates mutable fields. Enforces the same price/stock invariants as <see cref="Create"/>.
    /// </summary>
    public void Update(
        string name,
        string description,
        decimal price,
        string category,
        int stockQuantity,
        string? imageUrl,
        string? details = null)
    {
        if (price <= 0) throw new DomainException("Price must be greater than zero");
        if (stockQuantity < 0) throw new DomainException("Stock cannot be negative");

        Name = name.Trim();
        Description = description.Trim();
        Price = price;
        Category = category.Trim();
        StockQuantity = stockQuantity;
        ImageUrl = imageUrl;
        Details = details;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Decrements stock by <paramref name="quantity"/> units.</summary>
    public void ReduceStock(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Stock reduction quantity must be positive");
        if (quantity > StockQuantity)
            throw new DomainException($"Insufficient stock. Available: {StockQuantity}");
        StockQuantity -= quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Restores stock when an order cannot be persisted or payment setup fails.</summary>
    public void RestoreStock(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Stock restoration quantity must be positive");
        StockQuantity = checked(StockQuantity + quantity);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Soft-deletes the product by setting <see cref="IsActive"/> to <c>false</c>.</summary>
    public void Deactivate() { IsActive = false; UpdatedAt = DateTime.UtcNow; }
}