using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecureShop.API.Pages.Admin;
using SecureShop.API.Pages.Admin.Customers;
using SecureShop.API.Pages.Admin.Orders;
using SecureShop.API.Pages.Admin.Products;
using SecureShop.API.Pages.Admin.Reports;
using SecureShop.Domain.Entities;
using System.Security.Claims;

namespace SecureShop.UnitTests;

/// <summary>
/// For admin page models we call OnGet() directly, bypassing the
/// AdminPageModel.OnPageHandlerExecuting filter (which runs through the full
/// MVC pipeline). The OnGet methods only read from IConfiguration/TempData.
/// </summary>
internal static class AdminTestHelper
{
    public static IConfiguration Config(
        string apiBase = "http://localhost:8080",
        string? cloudName = null,
        string? uploadPreset = null)
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiBaseUrl"]).Returns(apiBase);
        if (cloudName != null)
            config.Setup(c => c["Cloudinary:CloudName"]).Returns(cloudName);
        if (uploadPreset != null)
            config.Setup(c => c["Cloudinary:UploadPreset"]).Returns(uploadPreset);
        return config.Object;
    }

    public static ITempDataDictionary EmptyTempData()
    {
        var ctx = new DefaultHttpContext();
        return new TempDataDictionary(ctx, Mock.Of<ITempDataProvider>());
    }

    public static ITempDataDictionary TempDataWith(string key, object value)
    {
        var ctx = new DefaultHttpContext();
        var td = new TempDataDictionary(ctx, Mock.Of<ITempDataProvider>()) { [key] = value };
        return td;
    }

    public static void SetupPageContext(PageModel model)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost", 8080);
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AdminIndexModel
// ════════════════════════════════════════════════════════════════════════════

public class AdminIndexModelTests
{
    [Fact]
    public void OnGet_SetsApiBaseUrlFromConfig()
    {
        var model = new AdminIndexModel(AdminTestHelper.Config("http://prod:8080"));
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.ApiBaseUrl.Should().Be("http://prod:8080");
    }

    [Fact]
    public void OnGet_NullConfig_DefaultsToLocalhost()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiBaseUrl"]).Returns((string?)null);
        var model = new AdminIndexModel(config.Object);
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.ApiBaseUrl.Should().Be("http://localhost:8080");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AdminProductsModel
// ════════════════════════════════════════════════════════════════════════════

public class AdminProductsModelTests
{
    [Fact]
    public void OnGet_SetsApiBaseUrl()
    {
        var model = new AdminProductsModel(AdminTestHelper.Config("http://api:5000"));
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.ApiBaseUrl.Should().Be("http://api:5000");
    }

    [Fact]
    public void OnGet_NullConfig_DefaultsToLocalhost()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiBaseUrl"]).Returns((string?)null);
        var model = new AdminProductsModel(config.Object);
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.ApiBaseUrl.Should().Be("http://localhost:8080");
    }

    [Fact]
    public void OnGet_LoadsSuccessMessageFromTempData()
    {
        var model = new AdminProductsModel(AdminTestHelper.Config());
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.TempDataWith("SuccessMessage", "Product created!");

        model.OnGet();

        model.SuccessMessage.Should().Be("Product created!");
    }

    [Fact]
    public void OnGet_NoTempDataMessage_SuccessMessageIsNull()
    {
        var model = new AdminProductsModel(AdminTestHelper.Config());
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.SuccessMessage.Should().BeNull();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AdminOrdersModel
// ════════════════════════════════════════════════════════════════════════════

public class AdminOrdersModelTests
{
    [Fact]
    public void OnGet_SetsApiBaseUrl()
    {
        var model = new AdminOrdersModel(AdminTestHelper.Config("http://orders-api"));
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.ApiBaseUrl.Should().Be("http://orders-api");
    }

    [Fact]
    public void OnGet_NullConfig_DefaultsToLocalhost()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiBaseUrl"]).Returns((string?)null);
        var model = new AdminOrdersModel(config.Object);
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.ApiBaseUrl.Should().Be("http://localhost:8080");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AdminCustomersModel
// ════════════════════════════════════════════════════════════════════════════

public class AdminCustomersModelTests
{
    [Fact]
    public void OnGet_SetsApiBaseUrl()
    {
        var model = new AdminCustomersModel(AdminTestHelper.Config());
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.ApiBaseUrl.Should().Be("http://localhost:8080");
    }

    [Fact]
    public void OnGet_NullConfig_DefaultsToLocalhost()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiBaseUrl"]).Returns((string?)null);
        var model = new AdminCustomersModel(config.Object);
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.ApiBaseUrl.Should().Be("http://localhost:8080");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AdminReportsModel
// ════════════════════════════════════════════════════════════════════════════

public class AdminReportsModelTests
{
    [Fact]
    public void OnGet_SetsApiBaseUrl()
    {
        var model = new AdminReportsModel(AdminTestHelper.Config("http://reports-api"));
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.ApiBaseUrl.Should().Be("http://reports-api");
    }

    [Fact]
    public void OnGet_NullConfig_DefaultsToLocalhost()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiBaseUrl"]).Returns((string?)null);
        var model = new AdminReportsModel(config.Object);
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.ApiBaseUrl.Should().Be("http://localhost:8080");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AdminOrderDetailsModel
// ════════════════════════════════════════════════════════════════════════════

public class AdminOrderDetailsModelTests
{
    [Fact]
    public void OnGet_SetsApiBaseUrlAndOrderId()
    {
        var model = new AdminOrderDetailsModel(AdminTestHelper.Config());
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet("order-abc-123");

        model.ApiBaseUrl.Should().Be("http://localhost:8080");
        model.OrderId.Should().Be("order-abc-123");
    }

    [Fact]
    public void OnGet_NullId_SetsOrderIdEmpty()
    {
        var model = new AdminOrderDetailsModel(AdminTestHelper.Config());
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet(null!);

        model.OrderId.Should().BeNull();
    }

    [Fact]
    public void OnGet_NullConfig_DefaultsToLocalhost()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiBaseUrl"]).Returns((string?)null);
        var model = new AdminOrderDetailsModel(config.Object);
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet("order-xyz");

        model.ApiBaseUrl.Should().Be("http://localhost:8080");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AdminProductCreateModel
// ════════════════════════════════════════════════════════════════════════════

public class AdminProductCreateModelTests
{
    [Fact]
    public void OnGet_SetsAllConfigProperties()
    {
        var model = new AdminProductCreateModel(
            AdminTestHelper.Config("http://api:8080", "mycloud", "my_preset"));
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.ApiBaseUrl.Should().Be("http://api:8080");
        model.CloudName.Should().Be("mycloud");
        model.UploadPreset.Should().Be("my_preset");
    }

    [Fact]
    public void OnGet_NullCloudinaryConfig_DefaultsToEmpty()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiBaseUrl"]).Returns("http://localhost:8080");
        config.Setup(c => c["Cloudinary:CloudName"]).Returns((string?)null);
        config.Setup(c => c["Cloudinary:UploadPreset"]).Returns((string?)null);

        var model = new AdminProductCreateModel(config.Object);
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.CloudName.Should().BeEmpty();
        model.UploadPreset.Should().BeEmpty();
    }

    [Fact]
    public void OnGet_NullApiBaseUrl_DefaultsToLocalhost()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiBaseUrl"]).Returns((string?)null);
        config.Setup(c => c["Cloudinary:CloudName"]).Returns("cloud");
        config.Setup(c => c["Cloudinary:UploadPreset"]).Returns("preset");

        var model = new AdminProductCreateModel(config.Object);
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet();

        model.ApiBaseUrl.Should().Be("http://localhost:8080");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AdminProductEditModel
// ════════════════════════════════════════════════════════════════════════════

public class AdminProductEditModelTests
{
    [Fact]
    public void OnGet_SetsAllConfigPropertiesAndProductId()
    {
        var model = new AdminProductEditModel(
            AdminTestHelper.Config("http://api:8080", "mycloud", "my_preset"));
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet("product-xyz-456");

        model.ApiBaseUrl.Should().Be("http://api:8080");
        model.ProductId.Should().Be("product-xyz-456");
        model.CloudName.Should().Be("mycloud");
        model.UploadPreset.Should().Be("my_preset");
    }

    [Fact]
    public void OnGet_NullCloudinaryConfig_DefaultsToEmpty()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiBaseUrl"]).Returns("http://localhost:8080");
        config.Setup(c => c["Cloudinary:CloudName"]).Returns((string?)null);
        config.Setup(c => c["Cloudinary:UploadPreset"]).Returns((string?)null);

        var model = new AdminProductEditModel(config.Object);
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet("product-abc");

        model.CloudName.Should().BeEmpty();
        model.UploadPreset.Should().BeEmpty();
        model.ProductId.Should().Be("product-abc");
    }

    [Fact]
    public void OnGet_NullApiBaseUrl_DefaultsToLocalhost()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ApiBaseUrl"]).Returns((string?)null);
        config.Setup(c => c["Cloudinary:CloudName"]).Returns("cloud");
        config.Setup(c => c["Cloudinary:UploadPreset"]).Returns("preset");

        var model = new AdminProductEditModel(config.Object);
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        model.OnGet("product-abc");

        model.ApiBaseUrl.Should().Be("http://localhost:8080");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AdminLogoutModel
// ════════════════════════════════════════════════════════════════════════════

public class AdminLogoutModelTests
{
    [Fact]
    public async Task OnGetAsync_SignsOutAndRedirectsToAdminLogin()
    {
        var logger = new Mock<ILogger<AdminLogoutModel>>();
        var model = new AdminLogoutModel(logger.Object);

        // Mock IAuthenticationService so HttpContext.SignOutAsync("AdminCookie") works
        var authService = new Mock<IAuthenticationService>();
        authService.Setup(s => s.SignOutAsync(
                It.IsAny<HttpContext>(), "AdminCookie", It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(authService.Object);
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));
        model.TempData = AdminTestHelper.EmptyTempData();

        var result = await model.OnGetAsync();

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("/admin/login");

        authService.Verify(s => s.SignOutAsync(
            It.IsAny<HttpContext>(), "AdminCookie", It.IsAny<AuthenticationProperties>()),
            Times.Once);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AdminLoginModel.OnGet
// ════════════════════════════════════════════════════════════════════════════

public class AdminLoginModelTests
{
    [Fact]
    public void OnGet_CompletesWithoutError()
    {
        var logger = new Mock<ILogger<AdminLoginModel>>();
        var store = new Mock<IUserStore<SecureShop.Domain.Entities.ApplicationUser>>();
        var userManager = new Mock<Microsoft.AspNetCore.Identity.UserManager<SecureShop.Domain.Entities.ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var signInManager = new Mock<Microsoft.AspNetCore.Identity.SignInManager<SecureShop.Domain.Entities.ApplicationUser>>(
            userManager.Object,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<Microsoft.AspNetCore.Identity.IUserClaimsPrincipalFactory<SecureShop.Domain.Entities.ApplicationUser>>().Object,
            null, null, null, null);

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
               .Returns(new System.Net.Http.HttpClient());
        var config = new Mock<IConfiguration>();

        var model = new AdminLoginModel(
            userManager.Object, signInManager.Object, logger.Object, config.Object, factory.Object);
        AdminTestHelper.SetupPageContext(model);
        model.TempData = AdminTestHelper.EmptyTempData();

        var act = () => model.OnGet();
        act.Should().NotThrow();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AdminLoginModel.OnPostAsync
// ════════════════════════════════════════════════════════════════════════════

public class AdminLoginModelPostTests
{
    private static (
        Mock<UserManager<ApplicationUser>> um,
        AdminLoginModel model,
        Mock<IAuthenticationService> authSvc)
    Create(IConfiguration? config = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var um    = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var signInManager = new Mock<SignInManager<ApplicationUser>>(
            um.Object,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>().Object,
            null, null, null, null);

        var logger  = new Mock<ILogger<AdminLoginModel>>();
        var authSvc = new Mock<IAuthenticationService>();
        authSvc.Setup(s => s.SignInAsync(
                It.IsAny<HttpContext>(), It.IsAny<string?>(),
                It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);

        var sp = new ServiceCollection()
            .AddSingleton(authSvc.Object)
            .BuildServiceProvider();

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        // Jwt config needs a real 32-byte secret so JwtSecurityTokenHandler can sign
        config ??= new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"]   = AccountTestHelper.TestSecret,
                ["Jwt:Issuer"]   = "test",
                ["Jwt:Audience"] = "test",
            })
            .Build();

        var model = new AdminLoginModel(
            um.Object, signInManager.Object, logger.Object, config, factory.Object);

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host   = new HostString("localhost", 8080);
        model.PageContext = new PageContext(
            new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()));
        model.TempData = AdminTestHelper.EmptyTempData();

        return (um, model, authSvc);
    }

    [Fact]
    public async Task OnPostAsync_UserNotFound_SetsErrorMessage()
    {
        var (um, model, _) = Create();
        um.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        model.Email    = "nobody@example.com";
        model.Password = "Pass123!";

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Be("Invalid admin credentials");
    }

    [Fact]
    public async Task OnPostAsync_WrongPassword_SetsErrorMessage()
    {
        var (um, model, _) = Create();
        var user = new ApplicationUser { Id = "u1", Email = "admin@example.com", FirstName = "A", LastName = "B" };
        um.Setup(u => u.FindByEmailAsync("admin@example.com")).ReturnsAsync(user);
        um.Setup(u => u.CheckPasswordAsync(user, "WrongPass!")).ReturnsAsync(false);
        model.Email    = "admin@example.com";
        model.Password = "WrongPass!";

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Be("Invalid admin credentials");
    }

    [Fact]
    public async Task OnPostAsync_NotInAdminRole_SetsAccessDeniedMessage()
    {
        var (um, model, _) = Create();
        var user = new ApplicationUser { Id = "u1", Email = "user@example.com", FirstName = "A", LastName = "B" };
        um.Setup(u => u.FindByEmailAsync("user@example.com")).ReturnsAsync(user);
        um.Setup(u => u.CheckPasswordAsync(user, "Pass123!")).ReturnsAsync(true);
        um.Setup(u => u.IsInRoleAsync(user, "Admin")).ReturnsAsync(false);
        model.Email    = "user@example.com";
        model.Password = "Pass123!";

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("Access denied");
    }

    [Fact]
    public async Task OnPostAsync_ValidAdmin_SignsInAndRedirectsToAdminRoot()
    {
        var (um, model, authSvc) = Create();
        var user = new ApplicationUser
        {
            Id = "u1", Email = "admin@example.com",
            FirstName = "Admin", LastName = "User"
        };
        um.Setup(u => u.FindByEmailAsync("admin@example.com")).ReturnsAsync(user);
        um.Setup(u => u.CheckPasswordAsync(user, "AdminPass123!")).ReturnsAsync(true);
        um.Setup(u => u.IsInRoleAsync(user, "Admin")).ReturnsAsync(true);
        um.Setup(u => u.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Admin" });
        model.Email    = "admin@example.com";
        model.Password = "AdminPass123!";

        var result = await model.OnPostAsync();

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/admin");
        authSvc.Verify(s => s.SignInAsync(
            It.IsAny<HttpContext>(), "AdminCookie",
            It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties?>()), Times.Once);
    }

    [Fact]
    public async Task OnPostAsync_ThrowsException_SetsErrorMessage()
    {
        var (um, model, _) = Create();
        um.Setup(u => u.FindByEmailAsync(It.IsAny<string>()))
          .ThrowsAsync(new Exception("db crash"));
        model.Email    = "admin@example.com";
        model.Password = "Pass123!";

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// AdminPageModel.OnPageHandlerExecuting
// ════════════════════════════════════════════════════════════════════════════

public class AdminPageModelFilterTests
{
    private static (DefaultHttpContext ctx, AdminIndexModel model) CreateWithAuthResult(
        AuthenticateResult authResult)
    {
        var authSvc = new Mock<IAuthenticationService>();
        authSvc.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), "AdminCookie"))
               .ReturnsAsync(authResult);
        authSvc.Setup(s => s.SignInAsync(
                It.IsAny<HttpContext>(), It.IsAny<string?>(),
                It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);
        authSvc.Setup(s => s.SignOutAsync(
                It.IsAny<HttpContext>(), It.IsAny<string?>(),
                It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);

        var sp = new ServiceCollection()
            .AddSingleton(authSvc.Object)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var model       = new AdminIndexModel(AdminTestHelper.Config());
        model.PageContext = new PageContext(
            new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()));
        model.TempData = AdminTestHelper.EmptyTempData();
        return (httpContext, model);
    }

    private static PageHandlerExecutingContext BuildFilterContext(
        PageModel model, DefaultHttpContext ctx)
    {
        var pageContext = new PageContext(
            new ActionContext(ctx, new RouteData(), new PageActionDescriptor()));
        return new PageHandlerExecutingContext(
            pageContext,
            new List<IFilterMetadata>(),
            handlerMethod: null,
            new Dictionary<string, object?>(),
            model);
    }

    [Fact]
    public void OnPageHandlerExecuting_AuthFails_RedirectsToAdminLogin()
    {
        var (ctx, model) = CreateWithAuthResult(AuthenticateResult.Fail("not authenticated"));
        var filterCtx    = BuildFilterContext(model, ctx);

        model.OnPageHandlerExecuting(filterCtx);

        filterCtx.Result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("/admin/login");
    }

    [Fact]
    public void OnPageHandlerExecuting_AuthSucceedsNoAdminRole_RedirectsToAdminLogin()
    {
        var identity  = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Regular") }, "AdminCookie");
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, "AdminCookie");

        var (ctx, model) = CreateWithAuthResult(AuthenticateResult.Success(ticket));
        var filterCtx    = BuildFilterContext(model, ctx);

        model.OnPageHandlerExecuting(filterCtx);

        filterCtx.Result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("/admin/login");
    }

    [Fact]
    public void OnPageHandlerExecuting_AuthSucceedsWithAdminRole_SetsAdminName()
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.Name, "Admin User") },
            "AdminCookie");
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, "AdminCookie");

        var (ctx, model) = CreateWithAuthResult(AuthenticateResult.Success(ticket));
        var filterCtx    = BuildFilterContext(model, ctx);

        model.OnPageHandlerExecuting(filterCtx);

        filterCtx.Result.Should().BeNull();
        model.AdminName.Should().Be("Admin User");
    }

    [Fact]
    public void OnPageHandlerExecuting_AuthSucceedsAdminRoleNoName_SetsDefaultAdminName()
    {
        // Admin role present but no Name claim → Identity.Name is null → uses "Administrator"
        var identity  = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }, "AdminCookie");
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, "AdminCookie");

        var (ctx, model) = CreateWithAuthResult(AuthenticateResult.Success(ticket));
        var filterCtx    = BuildFilterContext(model, ctx);

        model.OnPageHandlerExecuting(filterCtx);

        filterCtx.Result.Should().BeNull();
        model.AdminName.Should().Be("Administrator");
    }
}
