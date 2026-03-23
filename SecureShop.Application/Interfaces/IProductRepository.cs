using SecureShop.Domain.Entities;

namespace SecureShop.Application.Interfaces;

public interface IProductRepository
{
    Task<IEnumerable<Product>> GetAllAsync(string? category, int page, int pageSize);
    Task<Product?> GetByIdAsync(Guid id);
    Task<Product> CreateAsync(Product product);
    Task UpdateAsync(Product product);
    Task<bool> ExistsAsync(Guid id);
    Task<int> CountAsync(string? category);
}