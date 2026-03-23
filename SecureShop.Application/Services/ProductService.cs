using SecureShop.Application.DTOs.Products;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Exceptions;

namespace SecureShop.Application.Services;

public class ProductService
{
    private readonly IProductRepository _repo;
    private readonly ICacheService _cache;
    private const string CachePrefix = "product:";

    public ProductService(IProductRepository repo, ICacheService cache)
    {
        _repo = repo;
        _cache = cache;
    }

    public async Task<PagedProductsDto> GetAllAsync(string? category, int page, int pageSize)
    {
        var cacheKey = $"{CachePrefix}list:{category}:{page}:{pageSize}";
        var cached = await _cache.GetAsync<PagedProductsDto>(cacheKey);
        if (cached is not null) return cached;

        var products = await _repo.GetAllAsync(category, page, pageSize);
        var total = await _repo.CountAsync(category);

        var result = new PagedProductsDto(
            products.Select(MapToDto), total, page, pageSize,
            (int)Math.Ceiling(total / (double)pageSize));

        await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5));
        return result;
    }

    public async Task<ProductResponseDto> GetByIdAsync(Guid id)
    {
        var cacheKey = $"{CachePrefix}{id}";
        var cached = await _cache.GetAsync<ProductResponseDto>(cacheKey);
        if (cached is not null) return cached;

        var product = await _repo.GetByIdAsync(id)
            ?? throw new DomainException($"Product {id} not found");

        var dto = MapToDto(product);
        await _cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(10));
        return dto;
    }

    public async Task<ProductResponseDto> CreateAsync(CreateProductDto dto)
    {
        var product = Product.Create(dto.Name, dto.Description, dto.Price, dto.StockQuantity, dto.Category);
        await _repo.CreateAsync(product);
        return MapToDto(product);
    }

    public async Task UpdateAsync(Guid id, UpdateProductDto dto)
    {
        var product = await _repo.GetByIdAsync(id)
            ?? throw new DomainException($"Product {id} not found");

        product.Update(dto.Name, dto.Description, dto.Price, dto.Category);
        await _repo.UpdateAsync(product);
        await _cache.RemoveAsync($"{CachePrefix}{id}");
    }

    public async Task DeleteAsync(Guid id)
    {
        var product = await _repo.GetByIdAsync(id)
            ?? throw new DomainException($"Product {id} not found");

        product.Deactivate();
        await _repo.UpdateAsync(product);
        await _cache.RemoveAsync($"{CachePrefix}{id}");
    }

    private static ProductResponseDto MapToDto(Product p) =>
        new(p.Id, p.Name, p.Description, p.Price, p.StockQuantity, p.Category, p.IsActive, p.CreatedAt);
}