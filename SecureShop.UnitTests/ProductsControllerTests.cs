using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SecureShop.API.Controllers;
using SecureShop.Application.DTOs.Products;
using SecureShop.Application.Interfaces;
using SecureShop.Application.Services;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Exceptions;

namespace SecureShop.UnitTests;

public class ProductsControllerTests
{
    private static ProductService MakeSvc(Mock<IProductRepository> repo, Mock<ICacheService> cache)
        => new(repo.Object, cache.Object);

    private static ProductsController MakeController(ProductService svc)
        => new(svc, NullLogger<ProductsController>.Instance);

    private static Mock<IProductRepository> RepoWithProducts(params Product[] products)
    {
        var repo = new Mock<IProductRepository>();
        var list = products.ToList();
        repo.Setup(r => r.GetAllAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(list);
        repo.Setup(r => r.CountAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(list.Count);
        return repo;
    }

    // ─── GET /api/products ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsOkWithPagedResult()
    {
        var product = Product.Create("Widget", "desc", 9.99m, 10, "Electronics");
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<PagedProductsDto>(It.IsAny<string>())).ReturnsAsync((PagedProductsDto?)null);

        var repo = RepoWithProducts(product);
        var controller = MakeController(MakeSvc(repo, cache));

        var result = await controller.GetAll(null, null, 1, 10);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var paged = ok.Value.Should().BeOfType<PagedProductsDto>().Subject;
        paged.Items.Should().HaveCount(1);
        paged.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAll_WithCategory_PassesCategoryToService()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<PagedProductsDto>(It.IsAny<string>())).ReturnsAsync((PagedProductsDto?)null);
        var repo = RepoWithProducts();
        var controller = MakeController(MakeSvc(repo, cache));

        await controller.GetAll("Electronics", null, 1, 10);

        repo.Verify(r => r.GetAllAsync("Electronics", null, 1, 10), Times.Once);
    }

    [Fact]
    public async Task GetAll_WithSearch_PassesSearchToService()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<PagedProductsDto>(It.IsAny<string>())).ReturnsAsync((PagedProductsDto?)null);
        var repo = RepoWithProducts();
        var controller = MakeController(MakeSvc(repo, cache));

        await controller.GetAll(null, "laptop", 1, 10);

        repo.Verify(r => r.GetAllAsync(null, "laptop", 1, 10), Times.Once);
    }

    [Fact]
    public async Task GetAll_WhenExceptionThrown_Returns500()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<PagedProductsDto>(It.IsAny<string>())).ReturnsAsync((PagedProductsDto?)null);
        var repo = new Mock<IProductRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new Exception("DB error"));
        var controller = MakeController(MakeSvc(repo, cache));

        var result = await controller.GetAll(null, null, 1, 10);

        result.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }

    // ─── GET /api/products/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task GetById_ProductExists_ReturnsOk()
    {
        var product = Product.Create("Widget", "desc", 9.99m, 10, "Electronics");
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>())).ReturnsAsync((ProductResponseDto?)null);
        var repo = new Mock<IProductRepository>();
        repo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);

        var controller = MakeController(MakeSvc(repo, cache));
        var result = await controller.GetById(product.Id);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ProductResponseDto>().Subject;
        dto.Name.Should().Be("Widget");
    }

    [Fact]
    public async Task GetById_ProductNotFound_ReturnsDomainExceptionAs500()
    {
        var id = Guid.NewGuid();
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>())).ReturnsAsync((ProductResponseDto?)null);
        var repo = new Mock<IProductRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Product?)null);

        var controller = MakeController(MakeSvc(repo, cache));
        var result = await controller.GetById(id);

        // DomainException is not KeyNotFoundException, so falls through to 500
        result.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }

    // ─── POST /api/products ───────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidProduct_Returns201Created()
    {
        var cache = new Mock<ICacheService>();
        var repo = new Mock<IProductRepository>();
        repo.Setup(r => r.CreateAsync(It.IsAny<Product>())).ReturnsAsync((Product p) => p);

        var controller = MakeController(MakeSvc(repo, cache));
        var dto = new CreateProductDto("Widget", "desc", 9.99m, 10, "Electronics");

        var result = await controller.Create(dto);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var response = created.Value.Should().BeOfType<ProductResponseDto>().Subject;
        response.Name.Should().Be("Widget");
    }

    // ─── PUT /api/products/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task Update_ProductExists_ReturnsNoContent()
    {
        var product = Product.Create("Widget", "desc", 9.99m, 10, "Electronics");
        var cache = new Mock<ICacheService>();
        var repo = new Mock<IProductRepository>();
        repo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);

        var controller = MakeController(MakeSvc(repo, cache));
        var dto = new UpdateProductDto("Updated", "new desc", 19.99m, "Books", 5);

        var result = await controller.Update(product.Id, dto);

        result.Should().BeOfType<NoContentResult>();
    }

    // ─── DELETE /api/products/{id} ────────────────────────────────────────────

    [Fact]
    public async Task Delete_ProductExists_ReturnsNoContent()
    {
        var product = Product.Create("Widget", "desc", 9.99m, 10, "Electronics");
        var cache = new Mock<ICacheService>();
        var repo = new Mock<IProductRepository>();
        repo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);

        var controller = MakeController(MakeSvc(repo, cache));

        var result = await controller.Delete(product.Id);

        result.Should().BeOfType<NoContentResult>();
        product.IsActive.Should().BeFalse();
    }

    // ── GetById error paths ────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_RepoThrowsKeyNotFoundException_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>())).ReturnsAsync((ProductResponseDto?)null);
        var repo = new Mock<IProductRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ThrowsAsync(new KeyNotFoundException("not found"));

        var controller = MakeController(MakeSvc(repo, cache));
        var result = await controller.GetById(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetById_UnexpectedError_Returns500()
    {
        var id = Guid.NewGuid();
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>())).ThrowsAsync(new Exception("redis down"));
        var repo = new Mock<IProductRepository>();

        var controller = MakeController(MakeSvc(repo, cache));
        var result = await controller.GetById(id);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }

    // ── Create error paths ─────────────────────────────────────────────────────

    [Fact]
    public async Task Create_DomainException_ReturnsBadRequest()
    {
        // Price <= 0 triggers DomainException inside Product.Create
        var cache = new Mock<ICacheService>();
        var repo  = new Mock<IProductRepository>();
        var controller = MakeController(MakeSvc(repo, cache));
        var dto = new CreateProductDto("Widget", "desc", -1m, 10, "Electronics");

        var result = await controller.Create(dto);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_UnexpectedError_Returns500()
    {
        var cache = new Mock<ICacheService>();
        var repo  = new Mock<IProductRepository>();
        repo.Setup(r => r.CreateAsync(It.IsAny<Product>())).ThrowsAsync(new Exception("DB down"));
        var controller = MakeController(MakeSvc(repo, cache));
        var dto = new CreateProductDto("Widget", "desc", 9.99m, 10, "Electronics");

        var result = await controller.Create(dto);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }

    // ── Update error paths ─────────────────────────────────────────────────────

    [Fact]
    public async Task Update_RepoThrowsKeyNotFoundException_ReturnsNotFound()
    {
        var id    = Guid.NewGuid();
        var cache = new Mock<ICacheService>();
        var repo  = new Mock<IProductRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ThrowsAsync(new KeyNotFoundException("not found"));
        var controller = MakeController(MakeSvc(repo, cache));

        var result = await controller.Update(id, new UpdateProductDto("U", "d", 1m, "Cat", 1));

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Update_DomainException_ReturnsBadRequest()
    {
        var product = Product.Create("Widget", "desc", 9.99m, 10, "Electronics");
        var cache = new Mock<ICacheService>();
        var repo  = new Mock<IProductRepository>();
        repo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        var controller = MakeController(MakeSvc(repo, cache));

        // Price <= 0 triggers DomainException inside product.Update
        var result = await controller.Update(product.Id, new UpdateProductDto("U", "d", -5m, "Cat", 1));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Update_UnexpectedError_Returns500()
    {
        var id    = Guid.NewGuid();
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<ProductResponseDto>(It.IsAny<string>())).ThrowsAsync(new Exception("err"));
        var repo  = new Mock<IProductRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ThrowsAsync(new Exception("DB error"));
        var controller = MakeController(MakeSvc(repo, cache));

        var result = await controller.Update(id, new UpdateProductDto("U", "d", 1m, "Cat", 1));

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }

    // ── Delete error paths ─────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RepoThrowsKeyNotFoundException_ReturnsNotFound()
    {
        var id    = Guid.NewGuid();
        var cache = new Mock<ICacheService>();
        var repo  = new Mock<IProductRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ThrowsAsync(new KeyNotFoundException("not found"));
        var controller = MakeController(MakeSvc(repo, cache));

        var result = await controller.Delete(id);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Delete_DomainException_ReturnsBadRequest()
    {
        // Make the repo throw DomainException (unusual but covers the catch branch)
        var id    = Guid.NewGuid();
        var cache = new Mock<ICacheService>();
        var repo  = new Mock<IProductRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ThrowsAsync(new SecureShop.Domain.Exceptions.DomainException("domain err"));
        var controller = MakeController(MakeSvc(repo, cache));

        var result = await controller.Delete(id);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Delete_UnexpectedError_Returns500()
    {
        var id    = Guid.NewGuid();
        var cache = new Mock<ICacheService>();
        var repo  = new Mock<IProductRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ThrowsAsync(new Exception("DB error"));
        var controller = MakeController(MakeSvc(repo, cache));

        var result = await controller.Delete(id);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }
}
