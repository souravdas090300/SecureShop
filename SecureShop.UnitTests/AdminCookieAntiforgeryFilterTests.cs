using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moq;
using SecureShop.API.Middleware;

namespace SecureShop.UnitTests;

public class AdminCookieAntiforgeryFilterTests
{
    [Fact]
    public async Task AdminCookieMutation_ValidatesAntiforgeryToken()
    {
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery.Setup(service => service.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .Returns(Task.CompletedTask);
        var filter = new AdminCookieAntiforgeryFilter(
            antiforgery.Object,
            Mock.Of<ILogger<AdminCookieAntiforgeryFilter>>());
        var context = CreateContext("AdminCookie", "POST");
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(
                context, new List<IFilterMetadata>(), new object()));
        });

        antiforgery.Verify(service => service.ValidateRequestAsync(context.HttpContext), Times.Once);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AdminCookieMutation_InvalidTokenReturnsBadRequest()
    {
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery.Setup(service => service.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .ThrowsAsync(new AntiforgeryValidationException("Invalid token."));
        var filter = new AdminCookieAntiforgeryFilter(
            antiforgery.Object,
            Mock.Of<ILogger<AdminCookieAntiforgeryFilter>>());
        var context = CreateContext("AdminCookie", "DELETE");
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(
                context, new List<IFilterMetadata>(), new object()));
        });

        Assert.IsType<BadRequestObjectResult>(context.Result);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task BearerMutation_DoesNotRequireAntiforgeryToken()
    {
        var antiforgery = new Mock<IAntiforgery>();
        var filter = new AdminCookieAntiforgeryFilter(
            antiforgery.Object,
            Mock.Of<ILogger<AdminCookieAntiforgeryFilter>>());
        var context = CreateContext("Bearer", "PUT");
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(
                context, new List<IFilterMetadata>(), new object()));
        });

        antiforgery.Verify(
            service => service.ValidateRequestAsync(It.IsAny<HttpContext>()),
            Times.Never);
        Assert.True(nextCalled);
    }

    private static ActionExecutingContext CreateContext(
        string authenticationType,
        string method)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        httpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "admin") }, authenticationType));
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var context = new ActionExecutingContext(
            actionContext,
            filters,
            new Dictionary<string, object?>(),
            new object());

        return context;
    }
}
