using SecureShop.Application.DTOs.Products;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Exceptions;

namespace SecureShop.Application.Services;

/// <summary>
/// Application-layer service for product catalogue management.
/// Sits between the API controllers and the repository, adding
/// a Redis caching layer to reduce database load on read-heavy endpoints.
/// </summary>
public class ProductService
{
    public const int MaximumPageSize = 50;

    private readonly IProductRepository _repo;
    private readonly ICacheService _cache;

    private const string CachePrefix = "product:";

    public ProductService(IProductRepository repo, ICacheService cache)
    {
        _repo = repo;
        _cache = cache;
    }

    /// <summary>
    /// Returns a paginated list of active products.
    /// Results are cached for 5 minutes.
    /// </summary>
    public async Task<PagedProductsDto> GetAllAsync(string? category, string? search, int page, int pageSize)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page), "Page must be greater than zero.");
        if (pageSize is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Page size must be between 1 and {MaximumPageSize}.");

        var cacheKey = $"{CachePrefix}list:{category}:{search}:{page}:{pageSize}";
        var cached = await _cache.GetAsync<PagedProductsDto>(cacheKey);

        if (cached is not null) return cached;

        var products = await _repo.GetAllAsync(category, search, page, pageSize);
        var total = await _repo.CountAsync(category, search);

        var result = new PagedProductsDto(
            products.Select(MapToDto), total, page, pageSize,
            (int)Math.Ceiling(total / (double)pageSize));

        await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5));
        return result;
    }

    /// <summary>Returns a single product by its ID. Cached for 10 minutes.</summary>
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

    /// <summary>
    /// Creates a new product and invalidates all cached product list pages.
    /// </summary>
    public async Task<ProductResponseDto> CreateAsync(CreateProductDto dto)
    {
        var product = Product.Create(
            dto.Name,
            dto.Description,
            dto.Price,
            dto.StockQuantity,
            dto.Category,
            dto.ImageUrl,
            dto.Details);          // ← passes details through

        await _repo.CreateAsync(product);

        await _cache.RemoveByPrefixAsync($"{CachePrefix}list:");
        return MapToDto(product);
    }

    /// <summary>
    /// Updates an existing product's mutable fields and invalidates related cache entries.
    /// </summary>
    public async Task UpdateAsync(Guid id, UpdateProductDto dto)
    {
        var product = await _repo.GetByIdAsync(id)
            ?? throw new DomainException($"Product {id} not found");

        product.Update(
            dto.Name,
            dto.Description,
            dto.Price,
            dto.Category,
            dto.StockQuantity,
            dto.ImageUrl,
            dto.Details);          // ← passes details through

        await _repo.UpdateAsync(product);

        await _cache.RemoveAsync($"{CachePrefix}{id}");
        await _cache.RemoveByPrefixAsync($"{CachePrefix}list:");
    }

    /// <summary>
    /// Soft-deletes a product (sets <c>IsActive = false</c>) and invalidates related caches.
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        var product = await _repo.GetByIdAsync(id)
            ?? throw new DomainException($"Product {id} not found");

        product.Deactivate();
        await _repo.UpdateAsync(product);

        await _cache.RemoveAsync($"{CachePrefix}{id}");
        await _cache.RemoveByPrefixAsync($"{CachePrefix}list:");
    }

    /// <summary>
    /// Maps a <see cref="Product"/> domain entity to its read-model DTO.
    /// </summary>
    private static ProductResponseDto MapToDto(Product p) =>
        new(
            p.Id,
            p.Name,
            p.Description,
            p.Price,
            p.StockQuantity,
            p.Category,
            p.IsActive,
            p.CreatedAt,
            p.ImageUrl,
            p.Details);            // ← returns details to the API
}