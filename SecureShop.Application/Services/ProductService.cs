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
    private readonly IProductRepository _repo;
    private readonly ICacheService _cache;

    // All product cache keys start with this prefix so they can be bulk-invalidated.
    private const string CachePrefix = "product:";

    /// <summary>Initialises the service with its repository and cache dependencies.</summary>
    public ProductService(IProductRepository repo, ICacheService cache)
    {
        _repo = repo;
        _cache = cache;
    }

    /// <summary>
    /// Returns a paginated list of active products.
    /// Results are cached for 5 minutes; the cache key encodes all filter parameters
    /// so each unique combination is cached independently.
    /// </summary>
    /// <param name="category">Optional category filter.</param>
    /// <param name="search">Optional free-text search term.</param>
    /// <param name="page">1-based page index.</param>
    /// <param name="pageSize">Number of items per page.</param>
    public async Task<PagedProductsDto> GetAllAsync(string? category, string? search, int page, int pageSize)
    {
        // Build a deterministic cache key that includes every query dimension.
        var cacheKey = $"{CachePrefix}list:{category}:{search}:{page}:{pageSize}";
        var cached = await _cache.GetAsync<PagedProductsDto>(cacheKey);

        // Return immediately on a cache hit; avoids a database round-trip.
        if (cached is not null) return cached;

        // Run both DB queries concurrently to halve the round-trip time.
        var productsTask = _repo.GetAllAsync(category, search, page, pageSize);
        var totalTask = _repo.CountAsync(category, search);
        await Task.WhenAll(productsTask, totalTask);
        var products = productsTask.Result;
        var total = totalTask.Result;

        var result = new PagedProductsDto(
            products.Select(MapToDto), total, page, pageSize,
            // Calculate total page count, rounding up for partial pages.
            (int)Math.Ceiling(total / (double)pageSize));

        // Cache the result for 5 minutes to reduce repeat database queries.
        await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5));
        return result;
    }

    /// <summary>
    /// Returns a single product by its ID.
    /// Individual products are cached for 10 minutes.
    /// </summary>
    /// <param name="id">Product identifier.</param>
    /// <exception cref="DomainException">Thrown when no product with the given ID exists.</exception>
    public async Task<ProductResponseDto> GetByIdAsync(Guid id)
    {
        var cacheKey = $"{CachePrefix}{id}";
        var cached = await _cache.GetAsync<ProductResponseDto>(cacheKey);
        if (cached is not null) return cached;

        var product = await _repo.GetByIdAsync(id)
            ?? throw new DomainException($"Product {id} not found");

        var dto = MapToDto(product);
        // Cache individual product for longer than list results (fewer write invalidations).
        await _cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(10));
        return dto;
    }

    /// <summary>
    /// Creates a new product and invalidates all cached product list pages.
    /// </summary>
    /// <param name="dto">Product data supplied by the admin.</param>
    /// <returns>The newly created product as a response DTO.</returns>
    public async Task<ProductResponseDto> CreateAsync(CreateProductDto dto)
    {
        var product = Product.Create(dto.Name, dto.Description, dto.Price, dto.StockQuantity, dto.Category, dto.ImageUrl);
        await _repo.CreateAsync(product);

        // Invalidate list cache so the new product appears immediately on list endpoints.
        await _cache.RemoveByPrefixAsync($"{CachePrefix}list:");
        return MapToDto(product);
    }

    /// <summary>
    /// Updates an existing product's mutable fields and invalidates related cache entries.
    /// </summary>
    /// <param name="id">ID of the product to update.</param>
    /// <param name="dto">New field values.</param>
    /// <exception cref="DomainException">Thrown when the product does not exist.</exception>
    public async Task UpdateAsync(Guid id, UpdateProductDto dto)
    {
        var product = await _repo.GetByIdAsync(id)
            ?? throw new DomainException($"Product {id} not found");

        product.Update(dto.Name, dto.Description, dto.Price, dto.Category, dto.StockQuantity, dto.ImageUrl);
        await _repo.UpdateAsync(product);

        // Invalidate both the individual product cache entry and all list pages.
        await _cache.RemoveAsync($"{CachePrefix}{id}");
        await _cache.RemoveByPrefixAsync($"{CachePrefix}list:");
    }

    /// <summary>
    /// Soft-deletes a product (sets <c>IsActive = false</c>) and invalidates related caches.
    /// The record is retained in the database for historical order references.
    /// </summary>
    /// <param name="id">ID of the product to deactivate.</param>
    /// <exception cref="DomainException">Thrown when the product does not exist.</exception>
    public async Task DeleteAsync(Guid id)
    {
        var product = await _repo.GetByIdAsync(id)
            ?? throw new DomainException($"Product {id} not found");

        product.Deactivate();
        await _repo.UpdateAsync(product);

        // Purge cached data for this product and all paginated list results.
        await _cache.RemoveAsync($"{CachePrefix}{id}");
        await _cache.RemoveByPrefixAsync($"{CachePrefix}list:");
    }

    /// <summary>
    /// Maps a <see cref="Product"/> domain entity to its read-model DTO.
    /// </summary>
    private static ProductResponseDto MapToDto(Product p) =>
        new(p.Id, p.Name, p.Description, p.Price, p.StockQuantity, p.Category, p.IsActive, p.CreatedAt, p.ImageUrl);
}