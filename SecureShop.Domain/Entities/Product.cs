using SecureShop.Domain.Exceptions;

namespace SecureShop.Domain.Entities;

public class Product
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public int StockQuantity { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Product() { }

    public static Product Create(string name, string description,
        decimal price, int stock, string category)
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
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Update(string name, string description, decimal price, string category)
    {
        if (price <= 0) throw new DomainException("Price must be greater than zero");
        Name = name.Trim();
        Description = description.Trim();
        Price = price;
        Category = category.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void ReduceStock(int quantity)
    {
        if (quantity > StockQuantity)
            throw new DomainException($"Insufficient stock. Available: {StockQuantity}");
        StockQuantity -= quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate() { IsActive = false; UpdatedAt = DateTime.UtcNow; }
}