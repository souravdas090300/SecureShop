using FluentAssertions;
using Moq;
using SecureShop.Application.DTOs.Products;
using SecureShop.Application.Interfaces;
using SecureShop.Application.Services;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Exceptions;

namespace SecureShop.UnitTests;

public class ProductServiceTests
{
    private readonly Mock<IProductRepository> _repo = new();
    private readonly Mock<ICacheService> _cache = new();
    private ProductService Svc() => new(_repo.Object, _cache.Object);

    private static Product MakeProduct(string name = "Widget", decimal price = 9.99m, int stock = 10)
        => Product.Create(name, "desc", price, stock, "Electronics");

    // ─── GetAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_CacheHit_ReturnsCachedResult_WithoutHittingRepo()
    {
        var cached = new PagedProductsDto([], 0, 1, 12, 0);
        _cache.Setup(c => c.GetAsync<PagedProductsDto>(It.IsAny<string>())).ReturnsAsync(cached);

        var result = await Svc().GetAllAsync(null, null, 1, 12);

        result.Should().BeSameAs(cached);
        _repo.Verify(r => r.GetAllAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetAllAsync_CacheMiss_QueriesRepoAndStoresInCache()
    {
        _cache.Setup(c => c.GetAsync<PagedProductsDto>(It.IsAny<string>())).ReturnsAsync((PagedProductsDto?)null);
        _repo.Setup(r => r.GetAllAsync(null, null, 1, 12)).ReturnsAsync(new List<Product> { MakeProduct() });
        _repo.Setup(r => r.CountAsync(null, null)).ReturnsAsync(1);

        var result = await Svc().GetAllAsync(null, null, 1, 12);

        result.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
        _cache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<PagedProductsDto>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_CacheKeyIncludesAllParameters()
    {
        string? capturedKey = null;
        _cache.Setup(c => c.GetAsync<PagedProductsDto>(It.IsAny<string>()))
              .Callback<string>(k => capturedKey = k)
              .ReturnsAsync((PagedProductsDto?)null);
        _repo.Setup(r => r.GetAllAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
             .ReturnsAsync([]);
        _repo.Setup(r => r.CountAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(0);

        await Svc().GetAllAsync("Electronics", "widget", 2, 6);

        capturedKey.Should().Contain("Electronics").And.Contain("widget").And.Contain("2").And.Contain("6");
    }

    // ─── GetByIdAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_CacheHit_ReturnsCachedResult()
    {
        var id = Guid.NewGuid();
        var cached = new ProductResponseDto(id, "Widget", "desc", 9.99m, 10, "Electronics", true, DateTime.UtcNow);
        _cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>())).ReturnsAsync(cached);

        var result = await Svc().GetByIdAsync(id);

        result.Should().BeSameAs(cached);
        _repo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_CacheMiss_QueriesRepo()
    {
        var product = MakeProduct();
        _cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>())).ReturnsAsync((ProductResponseDto?)null);
        _repo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);

        var result = await Svc().GetByIdAsync(product.Id);

        result.Name.Should().Be("Widget");
        _cache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<ProductResponseDto>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_ProductNotFound_ThrowsDomainException()
    {
        var id = Guid.NewGuid();
        _cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>())).ReturnsAsync((ProductResponseDto?)null);
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Product?)null);

        var act = async () => await Svc().GetByIdAsync(id);

        await act.Should().ThrowAsync<DomainException>().WithMessage($"*{id}*");
    }

    // ─── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CreatesProductAndInvalidatesListCache()
    {
        var dto = new CreateProductDto("New Widget", "desc", 19.99m, 5, "Electronics");
        _repo.Setup(r => r.CreateAsync(It.IsAny<Product>())).ReturnsAsync((Product p) => p);

        var result = await Svc().CreateAsync(dto);

        result.Name.Should().Be("New Widget");
        result.Price.Should().Be(19.99m);
        _repo.Verify(r => r.CreateAsync(It.IsAny<Product>()), Times.Once);
        _cache.Verify(c => c.RemoveByPrefixAsync("product:list:"), Times.Once);
    }

    // ─── UpdateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ProductExists_UpdatesAndInvalidatesCache()
    {
        var product = MakeProduct();
        var dto = new UpdateProductDto("Updated", "new desc", 29.99m, "Books", 20);
        _repo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);

        await Svc().UpdateAsync(product.Id, dto);

        _repo.Verify(r => r.UpdateAsync(It.IsAny<Product>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync(It.Is<string>(k => k.Contains(product.Id.ToString()))), Times.Once);
        _cache.Verify(c => c.RemoveByPrefixAsync("product:list:"), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ProductNotFound_ThrowsDomainException()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Product?)null);

        var act = async () => await Svc().UpdateAsync(id, new UpdateProductDto("n", "d", 1m, "Cat", 0));
        await act.Should().ThrowAsync<DomainException>();
    }

    // ─── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ProductExists_DeactivatesAndInvalidatesCache()
    {
        var product = MakeProduct();
        _repo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);

        await Svc().DeleteAsync(product.Id);

        product.IsActive.Should().BeFalse();
        _repo.Verify(r => r.UpdateAsync(product), Times.Once);
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Once);
        _cache.Verify(c => c.RemoveByPrefixAsync("product:list:"), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ProductNotFound_ThrowsDomainException()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Product?)null);

        var act = async () => await Svc().DeleteAsync(id);
        await act.Should().ThrowAsync<DomainException>();
    }
}
