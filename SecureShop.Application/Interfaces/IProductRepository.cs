using SecureShop.Domain.Entities;

namespace SecureShop.Application.Interfaces;

/// <summary>
/// Data-access contract for <see cref="Product"/> persistence.
/// Implementations live in the Infrastructure layer (EF Core / PostgreSQL).
/// </summary>
public interface IProductRepository
{
    /// <summary>
    /// Returns a page of active products, optionally filtered by category and/or a search term.
    /// </summary>
    /// <param name="category">Case-insensitive category filter; pass <c>null</c> to return all categories.</param>
    /// <param name="search">Full-text search term applied to name and description; pass <c>null</c> to skip.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Maximum number of products to return per page.</param>
    Task<IEnumerable<Product>> GetAllAsync(string? category, string? search, int page, int pageSize);

    /// <summary>Returns the product with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Task<Product?> GetByIdAsync(Guid id);

    /// <summary>Persists a new <see cref="Product"/> and returns the saved entity.</summary>
    Task<Product> CreateAsync(Product product);

    /// <summary>Persists changes to an existing <see cref="Product"/>.</summary>
    Task UpdateAsync(Product product);

    /// <summary>Returns <c>true</c> when a product with the given <paramref name="id"/> exists.</summary>
    Task<bool> ExistsAsync(Guid id);

    /// <summary>
    /// Returns the total number of active products matching the optional filters.
    /// Used alongside <see cref="GetAllAsync"/> to calculate pagination metadata.
    /// </summary>
    Task<int> CountAsync(string? category, string? search);
}