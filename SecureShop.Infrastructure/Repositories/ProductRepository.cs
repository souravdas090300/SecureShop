using Microsoft.EntityFrameworkCore;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;

namespace SecureShop.Infrastructure;

public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _db;
    public ProductRepository(AppDbContext db) => _db = db;

    public async Task<IEnumerable<Product>> GetAllAsync(string? category, int page, int pageSize)
    {
        var query = _db.Products.AsQueryable();
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);
        return await query.OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
    }

    public async Task<Product?> GetByIdAsync(Guid id) =>
        await _db.Products.FirstOrDefaultAsync(p => p.Id == id);

    public async Task<Product> CreateAsync(Product product)
    {
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return product;
    }

    public async Task UpdateAsync(Product product)
    {
        _db.Products.Update(product);
        await _db.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Guid id) =>
        await _db.Products.AnyAsync(p => p.Id == id);

    public async Task<int> CountAsync(string? category)
    {
        var query = _db.Products.AsQueryable();
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);
        return await query.CountAsync();
    }
}