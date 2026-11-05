using Microsoft.EntityFrameworkCore;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;

namespace SecureShop.Infrastructure;

/// <summary>
/// EF Core implementation of <see cref="IProductRepository"/>.
/// All read queries filter on <c>IsActive == true</c> by default;
/// soft-deleted products are invisible to catalogue endpoints.
/// </summary>
public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _db;

    /// <summary>Initialises the repository with the scoped EF Core context.</summary>
    public ProductRepository(AppDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<IEnumerable<Product>> GetAllAsync(string? category, string? search, int page, int pageSize)
    {
        // Only surface active products to the storefront.
        var query = _db.Products.Where(p => p.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);

        // Simple substring search over Name and Description columns.
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.Description.Contains(search));

        return await query.OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<Product?> GetByIdAsync(Guid id) =>
        await _db.Products.FirstOrDefaultAsync(p => p.Id == id);

    /// <inheritdoc />
    public async Task<Product> CreateAsync(Product product)
    {
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return product;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Product product)
    {
        // Only mark the entity modified when it is detached. For tracked entities
        // (the normal path: GetByIdAsync and UpdateAsync share a scoped DbContext)
        // EF persists just the properties that actually changed — calling Update()
        // would mark every column modified and could write a stale StockQuantity
        // back over a concurrent atomic reservation.
        if (_db.Entry(product).State == EntityState.Detached)
            _db.Products.Update(product);
        await _db.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task<bool> TryReserveStockAsync(Guid productId, int quantity, CancellationToken ct = default)
    {
        // Single conditional UPDATE: the database evaluates StockQuantity >= quantity
        // and applies the decrement atomically, so two concurrent reservations cannot
        // both claim the last units. The global IsActive query filter also applies,
        // so deactivated products cannot be reserved.
        var rows = await _db.Products
            .Where(p => p.Id == productId && p.StockQuantity >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);
        return rows == 1;
    }

    /// <inheritdoc />
    public async Task RestoreStockAsync(Guid productId, int quantity, CancellationToken ct = default)
    {
        // IgnoreQueryFilters: a deactivated product must still get its stock back.
        await _db.Products
            .IgnoreQueryFilters()
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.StockQuantity, p => p.StockQuantity + quantity)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(Guid id) =>
        await _db.Products.AnyAsync(p => p.Id == id);

    /// <inheritdoc />
    public async Task<int> CountAsync(string? category, string? search)
    {
        // Mirror the same filters used in GetAllAsync for accurate total counts.
        var query = _db.Products.Where(p => p.IsActive).AsQueryable();
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.Description.Contains(search));
        return await query.CountAsync();
    }
}