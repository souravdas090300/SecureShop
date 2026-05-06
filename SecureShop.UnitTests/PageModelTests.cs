using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SecureShop.API.Pages;
using Microsoft.AspNetCore.Identity;
using SecureShop.API.Pages.Account;
using SecureShop.Domain.Entities;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SecureShop.UnitTests;

// ════════════════════════════════════════════════════════════════════════════
// ContactModel
// ════════════════════════════════════════════════════════════════════════════

public class ContactModelTests
{
    private static ContactModel Create()
    {
        var model = new ContactModel();
        PageModelTestHelper.SetupHttpContext(model);
        return model;
    }

    [Fact]
    public void OnGet_CompletesWithoutError()
    {
        var model = Create();
        var act = () => model.OnGet();
        act.Should().NotThrow();
    }

    [Fact]
    public void OnPost_ValidModel_SetsSuccessMessageAndClearsFields()
    {
        var model = Create();
        model.Name = "Jane Doe";
        model.Email = "jane@example.com";
        model.Subject = "Hello";
        model.Message = "This is a test message that is long enough.";

        var result = model.OnPost();

        result.Should().BeOfType<PageResult>();
        model.SuccessMessage.Should().NotBeNullOrEmpty();
        model.Name.Should().BeEmpty();
        model.Email.Should().BeEmpty();
        model.Subject.Should().BeEmpty();
        model.Message.Should().BeEmpty();
    }

    [Fact]
    public void OnPost_InvalidModel_SetsErrorMessage()
    {
        var model = Create();
        model.ModelState.AddModelError("Name", "Required");

        var result = model.OnPost();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
        model.SuccessMessage.Should().BeNull();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// CartModel
// ════════════════════════════════════════════════════════════════════════════

public class CartModelTests
{
    [Fact]
    public void OnGet_CompletesWithoutError()
    {
        var logger = new Mock<ILogger<CartModel>>();
        var model = new CartModel(logger.Object);
        PageModelTestHelper.SetupHttpContext(model);

        var act = () => model.OnGet();
        act.Should().NotThrow();
    }

    [Fact]
    public void OnGet_LogsPageLoad()
    {
        var logger = new Mock<ILogger<CartModel>>();
        var model = new CartModel(logger.Object);
        PageModelTestHelper.SetupHttpContext(model);

        model.OnGet();

        logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Cart")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void OnGet_NullUserIdentity_CompletesWithoutError()
    {
        // ClaimsPrincipal with no identities → User.Identity is null → null-coalescing branch
        var logger = new Mock<ILogger<CartModel>>();
        var model = new CartModel(logger.Object);
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal() };
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        var act = () => model.OnGet();
        act.Should().NotThrow();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// ProfileModel
// ════════════════════════════════════════════════════════════════════════════

public class ProfileModelTests
{
    private static ProfileModel CreateWithClaims(
        string email = "test@example.com",
        string firstName = "Jane",
        string lastName = "Doe",
        string userId = "user-42")
    {
        var logger = new Mock<ILogger<ProfileModel>>();
        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var model = new ProfileModel(logger.Object, userManager.Object);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email),
            new(ClaimTypes.GivenName, firstName),
            new(ClaimTypes.Surname, lastName),
            new(ClaimTypes.NameIdentifier, userId)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var httpContext = new DefaultHttpContext { User = principal };
        model.PageContext = new PageContext(new ActionContext(
            httpContext,
            new RouteData(),
            new PageActionDescriptor()));

        return model;
    }

    [Fact]
    public void OnGet_LoadsClaimsIntoProperties()
    {
        var model = CreateWithClaims("jane@example.com", "Jane", "Doe");

        model.OnGet();

        model.Email.Should().Be("jane@example.com");
        model.FirstName.Should().Be("Jane");
        model.LastName.Should().Be("Doe");
    }

    [Fact]
    public void OnGet_MissingClaims_SetsEmptyStrings()
    {
        // No claims principal
        var logger = new Mock<ILogger<ProfileModel>>();
        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var model = new ProfileModel(logger.Object, userManager.Object);
        // Use anonymous identity with no claims
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity())
        };
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        model.OnGet();

        model.Email.Should().BeEmpty();
        model.FirstName.Should().BeEmpty();
        model.LastName.Should().BeEmpty();
    }

    [Fact]
    public void OnPost_SetsErrorMessageAndReloadsClaims()
    {
        var model = CreateWithClaims("edit@example.com", "Edit", "Test");

        var result = model.OnPost();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
        // Claims should be reloaded
        model.Email.Should().Be("edit@example.com");
        model.FirstName.Should().Be("Edit");
        model.LastName.Should().Be("Test");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Account/OrdersModel
// ════════════════════════════════════════════════════════════════════════════

public class AccountOrdersModelTests
{
    private static OrdersModel CreateModel(
        HttpResponseMessage fakeResponse,
        string? cookieValue = "jwt-token-here")
    {
        var handler = new FakeHttpMessageHandler(fakeResponse);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
               .Returns(new HttpClient(handler));

        var config = new Mock<IConfiguration>();
        var logger = new Mock<ILogger<OrdersModel>>();
        var model = new OrdersModel(factory.Object, config.Object, logger.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost", 8080);

        if (cookieValue != null)
            httpContext.Request.Headers["Cookie"] = $"AuthToken={cookieValue}";

        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        return model;
    }

    [Fact]
    public async Task OnGetAsync_NoCookie_SetsErrorMessage()
    {
        var model = CreateModel(PageModelTestHelper.ServerError(), cookieValue: null);

        await model.OnGetAsync();

        model.ErrorMessage.Should().Contain("logged in");
        model.Orders.Should().BeEmpty();
    }

    [Fact]
    public async Task OnGetAsync_ApiSuccess_PopulatesOrders()
    {
        var payload = new[]
        {
            new { id = "3fa85f64-5717-4562-b3fc-2c963f66afa6", createdAt = DateTime.UtcNow, totalAmount = 49.99m, status = "Paid",
                  items = Array.Empty<object>() }
        };
        var model = CreateModel(PageModelTestHelper.JsonOk(payload));

        await model.OnGetAsync();

        model.Orders.Should().ContainSingle().Which.Status.Should().Be("Paid");
        model.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task OnGetAsync_Api401_SetsSessionExpiredMessage()
    {
        var model = CreateModel(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await model.OnGetAsync();

        model.ErrorMessage.Should().Contain("expired");
    }

    [Fact]
    public async Task OnGetAsync_ApiServerError_SetsErrorMessage()
    {
        var model = CreateModel(PageModelTestHelper.ServerError());

        await model.OnGetAsync();

        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnGetAsync_HttpClientThrows_SetsErrorMessage()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
               .Throws(new HttpRequestException("network error"));

        var model = new OrdersModel(factory.Object, new Mock<IConfiguration>().Object,
                                    new Mock<ILogger<OrdersModel>>().Object);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Cookie"] = "AuthToken=token";
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost", 8080);
        model.PageContext = new PageContext(new ActionContext(ctx, new RouteData(), new PageActionDescriptor()));

        await model.OnGetAsync();

        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AuthTestModel (simple page)
// ════════════════════════════════════════════════════════════════════════════

public class AuthTestModelTests
{
    [Fact]
    public void OnGet_CompletesWithoutError()
    {
        var model = new SecureShop.API.Pages.AuthTestModel();
        PageModelTestHelper.SetupHttpContext(model);

        var act = () => model.OnGet();
        act.Should().NotThrow();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AboutModel (simple page)  
// ════════════════════════════════════════════════════════════════════════════

public class AboutModelTests
{
    [Fact]
    public void OnGet_CompletesWithoutError()
    {
        var logger = new Mock<ILogger<SecureShop.API.Pages.AboutModel>>();
        var model = new SecureShop.API.Pages.AboutModel(logger.Object);
        PageModelTestHelper.SetupHttpContext(model);

        var act = () => model.OnGet();
        act.Should().NotThrow();
    }
}
