using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Moq;
using SecureShop.API.Pages;
using SecureShop.Application.DTOs.Auth;
using SecureShop.Application.DTOs.Orders;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Enums;
using SecureShop.Infrastructure.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SecureShop.UnitTests;

// ════════════════════════════════════════════════════════════════════════════
// NullCacheService
// ════════════════════════════════════════════════════════════════════════════

public class NullCacheServiceTests
{
    private readonly NullCacheService _svc = new();

    [Fact]
    public async Task GetAsync_AlwaysReturnsDefault()
    {
        var result = await _svc.GetAsync<string>("any-key");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ValueType_ReturnsDefault()
    {
        var result = await _svc.GetAsync<int>("key");
        result.Should().Be(0);
    }

    [Fact]
    public async Task SetAsync_CompletesWithoutThrowing()
    {
        var act = async () => await _svc.SetAsync("key", "value", TimeSpan.FromMinutes(5));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveAsync_CompletesWithoutThrowing()
    {
        var act = async () => await _svc.RemoveAsync("key");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_CompletesWithoutThrowing()
    {
        var act = async () => await _svc.RemoveByPrefixAsync("prefix:");
        await act.Should().NotThrowAsync();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// ApplicationUser entity
// ════════════════════════════════════════════════════════════════════════════

public class ApplicationUserTests
{
    [Fact]
    public void DefaultProperties_HaveExpectedValues()
    {
        var user = new ApplicationUser();

        user.FirstName.Should().Be(string.Empty);
        user.LastName.Should().Be(string.Empty);
        user.Orders.Should().BeEmpty();
        user.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Properties_CanBeAssigned()
    {
        var user = new ApplicationUser
        {
            FirstName = "Jane",
            LastName = "Doe"
        };

        user.FirstName.Should().Be("Jane");
        user.LastName.Should().Be("Doe");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Helpers shared by page model tests
// ════════════════════════════════════════════════════════════════════════════

internal sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(response);
}

internal static class PageModelTestHelper
{
    public static void SetupHttpContext(PageModel model, string scheme = "http", int port = 8080)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = scheme;
        httpContext.Request.Host = new HostString("localhost", port);

        model.PageContext = new PageContext(new ActionContext(
            httpContext,
            new RouteData(),
            new PageActionDescriptor()));
    }

    public static HttpResponseMessage JsonOk(object payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };

    public static HttpResponseMessage ServerError() =>
        new(HttpStatusCode.InternalServerError);
}

// ════════════════════════════════════════════════════════════════════════════
// ProductsModel
// ════════════════════════════════════════════════════════════════════════════

public class ProductsModelTests
{
    private static ProductsModel CreateModel(HttpResponseMessage fakeResponse)
    {
        var handler = new FakeHttpMessageHandler(fakeResponse);
        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var config = new Mock<IConfiguration>();
        var model = new ProductsModel(factory.Object, config.Object);
        PageModelTestHelper.SetupHttpContext(model);
        return model;
    }

    [Fact]
    public async Task OnGetAsync_ApiReturnsSuccess_PopulatesProducts()
    {
        var payload = new
        {
            items = new[]
            {
                new { id = Guid.NewGuid(), name = "Widget", description = "desc",
                      price = 9.99m, stockQuantity = 5, category = "Electronics",
                      isActive = true, createdAt = DateTime.UtcNow, imageUrl = (string?)null }
            },
            totalCount = 1,
            page = 1,
            pageSize = 12,
            totalPages = 1
        };
        var model = CreateModel(PageModelTestHelper.JsonOk(payload));

        await model.OnGetAsync(1);

        model.Products.Should().ContainSingle().Which.Name.Should().Be("Widget");
        model.TotalCount.Should().Be(1);
        model.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task OnGetAsync_ApiReturnsError_LeavesProductsEmpty()
    {
        var model = CreateModel(PageModelTestHelper.ServerError());

        await model.OnGetAsync(1);

        model.Products.Should().BeEmpty();
    }

    [Fact]
    public async Task OnGetAsync_HttpClientThrows_LeavesProductsEmpty()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
               .Throws(new HttpRequestException("network error"));

        var model = new ProductsModel(factory.Object, new Mock<IConfiguration>().Object);
        PageModelTestHelper.SetupHttpContext(model);

        await model.OnGetAsync(1);

        model.Products.Should().BeEmpty();
    }

    [Fact]
    public async Task OnGetAsync_PageLessThanOne_NormalisesTo1()
    {
        var model = CreateModel(PageModelTestHelper.ServerError());

        await model.OnGetAsync(0);

        model.CurrentPage.Should().Be(1);
    }

    [Fact]
    public async Task OnGetAsync_EmptyItems_LeavesProductsEmpty()
    {
        var payload = new
        {
            items = Array.Empty<object>(),
            totalCount = 0,
            page = 1,
            pageSize = 12,
            totalPages = 0
        };
        var model = CreateModel(PageModelTestHelper.JsonOk(payload));

        await model.OnGetAsync(1);

        model.Products.Should().BeEmpty();
        model.TotalCount.Should().Be(0);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// IndexModel
// ════════════════════════════════════════════════════════════════════════════

public class IndexModelTests
{
    private static IndexModel CreateModel(HttpResponseMessage fakeResponse, string? apiBase = "http://localhost:8080")
    {
        var handler = new FakeHttpMessageHandler(fakeResponse);
        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiBaseUrl"]).Returns(apiBase);

        var model = new IndexModel(factory.Object, config.Object);
        PageModelTestHelper.SetupHttpContext(model);
        return model;
    }

    [Fact]
    public async Task OnGetAsync_ApiReturnsSuccess_PopulatesFeaturedProducts()
    {
        var payload = new
        {
            items = new[]
            {
                new { id = Guid.NewGuid(), name = "Featured Widget", description = "desc",
                      price = 19.99m, stockQuantity = 3, category = "Books",
                      isActive = true, createdAt = DateTime.UtcNow, imageUrl = (string?)null }
            },
            totalCount = 1,
            page = 1,
            pageSize = 8,
            totalPages = 1
        };
        var model = CreateModel(PageModelTestHelper.JsonOk(payload));

        await model.OnGetAsync();

        model.FeaturedProducts.Should().ContainSingle().Which.Name.Should().Be("Featured Widget");
    }

    [Fact]
    public async Task OnGetAsync_ApiReturnsError_LeavesFeaturedProductsEmpty()
    {
        var model = CreateModel(PageModelTestHelper.ServerError());

        await model.OnGetAsync();

        model.FeaturedProducts.Should().BeEmpty();
    }

    [Fact]
    public async Task OnGetAsync_HttpClientThrows_LeavesFeaturedProductsEmpty()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
               .Throws(new HttpRequestException("network error"));

        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiBaseUrl"]).Returns("http://localhost:8080");

        var model = new IndexModel(factory.Object, config.Object);
        PageModelTestHelper.SetupHttpContext(model);

        await model.OnGetAsync();

        model.FeaturedProducts.Should().BeEmpty();
    }

    [Fact]
    public async Task OnGetAsync_NoApiBaseUrl_FallsBackToDefault()
    {
        // When config["ApiBaseUrl"] is null, IndexModel falls back to localhost:8080
        var model = CreateModel(PageModelTestHelper.ServerError(), apiBase: null);

        // Should not throw - just returns empty list
        var act = async () => await model.OnGetAsync();
        await act.Should().NotThrowAsync();
        model.FeaturedProducts.Should().BeEmpty();
    }

    [Fact]
    public async Task OnGetAsync_NullJsonResult_LeavesFeaturedProductsEmpty()
    {
        // 200 OK with "null" body → JsonSerializer returns null → null-coalescing branch
        var nullResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(
                "null", System.Text.Encoding.UTF8, "application/json")
        };
        var model = CreateModel(nullResponse);

        await model.OnGetAsync();

        model.FeaturedProducts.Should().BeEmpty();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// GoogleSignInDto
// ════════════════════════════════════════════════════════════════════════════

public class GoogleSignInDtoTests
{
    [Fact]
    public void IdToken_SetAndGet()
    {
        var dto = new GoogleSignInDto { IdToken = "google-id-token-abc" };
        dto.IdToken.Should().Be("google-id-token-abc");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Application.DTOs.Orders.OrderResponseDto  (covers remaining uncovered properties)
// ════════════════════════════════════════════════════════════════════════════

public class OrderResponseDtoTests
{
    [Fact]
    public void AllProperties_AreCoverable()
    {
        var id       = Guid.NewGuid();
        var created  = DateTime.UtcNow;
        var items    = new List<OrderItemResponseDto>();
        var dto      = new OrderResponseDto(
            id, "user-1", "customer@example.com",
            items, OrderStatus.Pending,
            99.99m, "pi_stripe_secret", created);

        dto.Id.Should().Be(id);
        dto.UserId.Should().Be("user-1");
        dto.CustomerEmail.Should().Be("customer@example.com");
        dto.Items.Should().BeSameAs(items);
        dto.Status.Should().Be(OrderStatus.Pending);
        dto.TotalAmount.Should().Be(99.99m);
        dto.StripePaymentIntentId.Should().Be("pi_stripe_secret");
        dto.CreatedAt.Should().Be(created);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Account.OrderItemDto  (covers ProductName, Quantity, Price properties)
// ════════════════════════════════════════════════════════════════════════════

public class AccountOrderItemDtoTests
{
    [Fact]
    public void AllProperties_SetAndGet()
    {
        var dto = new SecureShop.API.Pages.Account.OrderItemDto
        {
            ProductName = "Test Widget",
            Quantity    = 3,
            UnitPrice   = 19.99m
        };

        dto.ProductName.Should().Be("Test Widget");
        dto.Quantity.Should().Be(3);
        dto.UnitPrice.Should().Be(19.99m);
    }
}
