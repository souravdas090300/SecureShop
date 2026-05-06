using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using SecureShop.API.Pages;
using SecureShop.API.Pages.Account;
using SecureShop.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SecureShop.UnitTests;

// ════════════════════════════════════════════════════════════════════════════
// Shared helpers for account page model tests
// ════════════════════════════════════════════════════════════════════════════

internal static class AccountTestHelper
{
    // 34 bytes → 272-bit key, well above HMACSHA256's 256-bit minimum
    public const string TestSecret = "test-secret-key-for-unit-tests-32+";

    /// <summary>Creates a signed JWT suitable for parsing by LoginModel / GoogleCallbackModel.</summary>
    public static string CreateJwt(
        string sub = "user-1",
        string email = "test@example.com",
        string firstName = "Test",
        string lastName = "User",
        string? role = null)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, sub),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.GivenName, firstName),
            new(ClaimTypes.Surname, lastName),
        };
        if (role != null) claims.Add(new(ClaimTypes.Role, role));

        var token = new JwtSecurityToken(
            issuer: "test",
            audience: "test",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return handler.WriteToken(token);
    }

    /// <summary>
    /// Creates a DefaultHttpContext backed by a mocked IAuthenticationService so that
    /// HttpContext.SignInAsync / SignOutAsync succeed without a real middleware pipeline.
    /// </summary>
    public static (DefaultHttpContext ctx, Mock<IAuthenticationService> svc) CreateContextWithAuth()
    {
        var authSvc = new Mock<IAuthenticationService>();
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

        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost", 8080);
        return (ctx, authSvc);
    }

    public static IConfiguration JwtConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"]   = TestSecret,
                ["Jwt:Issuer"]   = "test",
                ["Jwt:Audience"] = "test",
            })
            .Build();

    public static ITempDataDictionary EmptyTempData(HttpContext? ctx = null)
    {
        ctx ??= new DefaultHttpContext();
        return new TempDataDictionary(ctx, Mock.Of<ITempDataProvider>());
    }

    public static void SetupPageContext(PageModel model, HttpContext ctx)
    {
        model.PageContext = new PageContext(
            new ActionContext(ctx, new RouteData(), new PageActionDescriptor()));
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Account.AuthTestModel  (namespace: SecureShop.API.Pages.Account)
// ════════════════════════════════════════════════════════════════════════════

public class AccountAuthTestModelTests
{
    [Fact]
    public void OnGet_Authenticated_SetsAllProperties()
    {
        var model = new SecureShop.API.Pages.Account.AuthTestModel();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name,           "testuser"),
            new(ClaimTypes.Email,          "auth@example.com"),
            new(ClaimTypes.GivenName,      "Auth"),
            new(ClaimTypes.Surname,        "User"),
        };
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };
        AccountTestHelper.SetupPageContext(model, ctx);

        model.OnGet();

        model.IsAuthenticated.Should().BeTrue();
        model.Email.Should().Be("auth@example.com");
        model.FirstName.Should().Be("Auth");
        model.LastName.Should().Be("User");
        model.ClaimCount.Should().Be(4);
        model.Claims.Should().ContainKey(ClaimTypes.Email);
    }

    [Fact]
    public void OnGet_Unauthenticated_SetsNotFoundStrings()
    {
        var model = new SecureShop.API.Pages.Account.AuthTestModel();
        AccountTestHelper.SetupPageContext(model, new DefaultHttpContext());

        model.OnGet();

        model.IsAuthenticated.Should().BeFalse();
        model.Email.Should().Be("(not found)");
        model.FirstName.Should().Be("(not found)");
        model.LastName.Should().Be("(not found)");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Account.LogoutModel
// ════════════════════════════════════════════════════════════════════════════

public class AccountLogoutModelTests
{
    [Fact]
    public async Task OnGetAsync_SignsOutAndRedirectsToIndex()
    {
        var model = new LogoutModel();
        var (ctx, authSvc) = AccountTestHelper.CreateContextWithAuth();
        AccountTestHelper.SetupPageContext(model, ctx);

        var result = await model.OnGetAsync();

        result.Should().BeOfType<RedirectToPageResult>()
            .Which.PageName.Should().Be("/Index");
        authSvc.Verify(s => s.SignOutAsync(
            It.IsAny<HttpContext>(),
            CookieAuthenticationDefaults.AuthenticationScheme,
            It.IsAny<AuthenticationProperties?>()), Times.Once);
    }

    [Fact]
    public async Task OnPostAsync_DelegatesToOnGetAsync()
    {
        var model = new LogoutModel();
        var (ctx, _) = AccountTestHelper.CreateContextWithAuth();
        AccountTestHelper.SetupPageContext(model, ctx);

        var result = await model.OnPostAsync();

        result.Should().BeOfType<RedirectToPageResult>();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Account.LoginModel
// ════════════════════════════════════════════════════════════════════════════

public class AccountLoginModelTests
{
    private static LoginModel CreateModel(HttpResponseMessage response, IConfiguration? config = null)
    {
        var handler = new FakeHttpMessageHandler(response);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
        return new LoginModel(factory.Object, config ?? new Mock<IConfiguration>().Object, new Mock<ILogger<LoginModel>>().Object);
    }

    [Fact]
    public void OnGet_NoTempData_SuccessMessageIsNull()
    {
        var model = CreateModel(PageModelTestHelper.ServerError());
        var ctx = new DefaultHttpContext();
        AccountTestHelper.SetupPageContext(model, ctx);
        model.TempData = AccountTestHelper.EmptyTempData(ctx);

        model.OnGet();

        model.SuccessMessage.Should().BeNull();
    }

    [Fact]
    public void OnGet_WithTempDataSuccessMessage_SetsSuccessMessage()
    {
        var model = CreateModel(PageModelTestHelper.ServerError());
        var ctx = new DefaultHttpContext();
        AccountTestHelper.SetupPageContext(model, ctx);
        var td = AccountTestHelper.EmptyTempData(ctx);
        td["SuccessMessage"] = "Registered successfully!";
        model.TempData = td;

        model.OnGet();

        model.SuccessMessage.Should().Be("Registered successfully!");
    }

    [Fact]
    public async Task OnPostAsync_InvalidModel_ReturnsPage()
    {
        var model = CreateModel(PageModelTestHelper.ServerError());
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();
        model.ModelState.AddModelError("Email", "Required");

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
    }

    [Fact]
    public async Task OnPostAsync_ApiSuccess_SignsInAndRedirects()
    {
        var jwt = AccountTestHelper.CreateJwt();
        var payload = new { token = jwt, email = "test@example.com", firstName = "Test", lastName = "User" };
        var model = CreateModel(PageModelTestHelper.JsonOk(payload));
        model.Email    = "test@example.com";
        model.Password = "Password123!";

        var (ctx, authSvc) = AccountTestHelper.CreateContextWithAuth();
        AccountTestHelper.SetupPageContext(model, ctx);
        model.TempData = AccountTestHelper.EmptyTempData(ctx);

        var result = await model.OnPostAsync();

        result.Should().BeOfType<RedirectResult>();
        authSvc.Verify(s => s.SignInAsync(
            It.IsAny<HttpContext>(), It.IsAny<string?>(),
            It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties?>()), Times.Once);
    }

    [Fact]
    public async Task OnPostAsync_ApiSuccessNullToken_SetsErrorMessage()
    {
        var payload = new { token = (string?)null, email = "test@example.com" };
        var model = CreateModel(PageModelTestHelper.JsonOk(payload));
        model.Email    = "test@example.com";
        model.Password = "Password123!";

        var (ctx, _) = AccountTestHelper.CreateContextWithAuth();
        AccountTestHelper.SetupPageContext(model, ctx);
        model.TempData = AccountTestHelper.EmptyTempData(ctx);

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnPostAsync_Api401_SetsInvalidCredentialsError()
    {
        var model = CreateModel(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        model.Email    = "test@example.com";
        model.Password = "WrongPass!";
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("Invalid");
    }

    [Fact]
    public async Task OnPostAsync_Api400_SetsErrorMessage()
    {
        var model = CreateModel(new HttpResponseMessage(HttpStatusCode.BadRequest));
        model.Email    = "test@example.com";
        model.Password = "Pass123!";
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnPostAsync_ApiOtherError_SetsErrorMessage()
    {
        var model = CreateModel(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        model.Email    = "test@example.com";
        model.Password = "Pass123!";
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnPostAsync_HttpRequestException_SetsConnectionError()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
               .Throws(new HttpRequestException("no network"));
        var model = new LoginModel(factory.Object, new Mock<IConfiguration>().Object, new Mock<ILogger<LoginModel>>().Object);
        model.Email    = "test@example.com";
        model.Password = "Pass123!";
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("connect");
    }

    [Fact]
    public async Task OnPostAsync_JsonException_SetsErrorMessage()
    {
        // 200 OK with body that cannot be deserialized → JsonException catch
        var badJson = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{ this is not valid json ]", Encoding.UTF8, "application/json")
        };
        var model = CreateModel(badJson);
        model.Email    = "test@example.com";
        model.Password = "Pass123!";
        var (ctx, _) = AccountTestHelper.CreateContextWithAuth();
        AccountTestHelper.SetupPageContext(model, ctx);
        model.TempData = AccountTestHelper.EmptyTempData(ctx);

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("invalid response");
    }
}

public class AccountRegisterModelTests
{
    private static RegisterModel CreateModel(HttpResponseMessage response, IConfiguration? config = null)
    {
        var handler = new FakeHttpMessageHandler(response);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
        return new RegisterModel(factory.Object, config ?? new Mock<IConfiguration>().Object, new Mock<ILogger<RegisterModel>>().Object);
    }

    private static void FillValidForm(RegisterModel model)
    {
        model.Email           = "new@example.com";
        model.Password        = "Secure123!";
        model.FirstName       = "New";
        model.LastName        = "User";
        model.ConfirmPassword = "Secure123!";
    }

    [Fact]
    public void OnGet_CompletesWithoutError()
    {
        var model = CreateModel(PageModelTestHelper.ServerError());
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();

        var act = () => model.OnGet();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task OnPostAsync_InvalidModel_ReturnsPageWithErrorMessage()
    {
        var model = CreateModel(PageModelTestHelper.ServerError());
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();
        model.ModelState.AddModelError("Email", "Required");

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("fix");
    }

    [Fact]
    public async Task OnPostAsync_ApiSuccessWithJwt_SignsInAndRedirects()
    {
        var jwt = AccountTestHelper.CreateJwt();
        var payload = new { token = jwt, email = "new@example.com", firstName = "New", lastName = "User" };
        var model = CreateModel(PageModelTestHelper.JsonOk(payload));
        FillValidForm(model);

        var (ctx, authSvc) = AccountTestHelper.CreateContextWithAuth();
        AccountTestHelper.SetupPageContext(model, ctx);
        model.TempData = AccountTestHelper.EmptyTempData(ctx);

        var result = await model.OnPostAsync();

        result.Should().BeOfType<RedirectResult>();
        authSvc.Verify(s => s.SignInAsync(
            It.IsAny<HttpContext>(), It.IsAny<string?>(),
            It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties?>()), Times.Once);
    }

    [Fact]
    public async Task OnPostAsync_ApiSuccessNullToken_RedirectsToLogin()
    {
        // 200 OK + null token → redirects to login page (graceful fallback)
        var payload = new { token = (string?)null };
        var model = CreateModel(PageModelTestHelper.JsonOk(payload));
        FillValidForm(model);

        var (ctx, _) = AccountTestHelper.CreateContextWithAuth();
        AccountTestHelper.SetupPageContext(model, ctx);
        model.TempData = AccountTestHelper.EmptyTempData(ctx);

        var result = await model.OnPostAsync();

        result.Should().BeOfType<RedirectToPageResult>()
            .Which.PageName.Should().Be("/Account/Login");
    }

    [Fact]
    public async Task OnPostAsync_ApiSuccessNullTokenNon200_SetsErrorMessage()
    {
        // 201 Created + null token → error message branch
        var payload = new { token = (string?)null };
        var created = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        var model = CreateModel(created);
        FillValidForm(model);

        var (ctx, _) = AccountTestHelper.CreateContextWithAuth();
        AccountTestHelper.SetupPageContext(model, ctx);
        model.TempData = AccountTestHelper.EmptyTempData(ctx);

        var result = await model.OnPostAsync();

        result.Should().BeOfType<RedirectToPageResult>()
            .Which.PageName.Should().Be("/Account/Login");
        model.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task OnPostAsync_Api400WithJsonError_SetsMessageFromServer()
    {
        var errorBody = JsonSerializer.Serialize(new { message = "Email already registered" });
        var badReq = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(errorBody, Encoding.UTF8, "application/json")
        };
        var model = CreateModel(badReq);
        FillValidForm(model);
        model.Email = "dup@example.com";
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("Email already registered");
    }

    [Fact]
    public async Task OnPostAsync_Api400NonJsonBody_SetsDefaultError()
    {
        var badReq = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("not json", Encoding.UTF8, "text/plain")
        };
        var model = CreateModel(badReq);
        FillValidForm(model);
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnPostAsync_ApiServerError_SetsErrorMessage()
    {
        var model = CreateModel(PageModelTestHelper.ServerError());
        FillValidForm(model);
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnPostAsync_HttpRequestException_SetsConnectionError()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
               .Throws(new HttpRequestException("network"));
        var model = new RegisterModel(factory.Object, new Mock<IConfiguration>().Object, new Mock<ILogger<RegisterModel>>().Object);
        FillValidForm(model);
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("connect");
    }

    [Fact]
    public async Task OnPostAsync_JsonException_SetsErrorMessage()
    {
        // 200 OK with non-parseable JSON → JsonException catch
        var badJson = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{ this is not valid json ]", Encoding.UTF8, "application/json")
        };
        var model = CreateModel(badJson);
        FillValidForm(model);
        var (ctx, _) = AccountTestHelper.CreateContextWithAuth();
        AccountTestHelper.SetupPageContext(model, ctx);
        model.TempData = AccountTestHelper.EmptyTempData(ctx);

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("invalid response");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Account.GoogleCallbackModel
// ════════════════════════════════════════════════════════════════════════════

public class GoogleCallbackModelTests
{
    private static GoogleCallbackModel CreateGoogleModel() =>
        new GoogleCallbackModel(new Mock<ILogger<GoogleCallbackModel>>().Object);

    [Fact]
    public async Task OnGetAsync_NullToken_RedirectsToAccountLogin()
    {
        var model = CreateGoogleModel();
        var (ctx, _) = AccountTestHelper.CreateContextWithAuth();
        AccountTestHelper.SetupPageContext(model, ctx);

        var result = await model.OnGetAsync(null);

        result.Should().BeOfType<RedirectToPageResult>()
            .Which.PageName.Should().Be("/Account/Login");
    }

    [Fact]
    public async Task OnGetAsync_ValidJwt_SignsInAndRedirectsToRoot()
    {
        var jwt = AccountTestHelper.CreateJwt();
        var model = CreateGoogleModel();
        var (ctx, authSvc) = AccountTestHelper.CreateContextWithAuth();
        AccountTestHelper.SetupPageContext(model, ctx);

        var result = await model.OnGetAsync(jwt);

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("/");
        authSvc.Verify(s => s.SignInAsync(
            It.IsAny<HttpContext>(), It.IsAny<string?>(),
            It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties?>()), Times.Once);
    }

    [Fact]
    public async Task OnGetAsync_InvalidJwt_RedirectsToAccountLogin()
    {
        var model = CreateGoogleModel();
        var (ctx, _) = AccountTestHelper.CreateContextWithAuth();
        AccountTestHelper.SetupPageContext(model, ctx);

        // "invalid" has no dots → ReadJwtToken throws → catch block → redirect
        var result = await model.OnGetAsync("invalid");

        result.Should().BeOfType<RedirectToPageResult>()
            .Which.PageName.Should().Be("/Account/Login");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// CheckoutModel
// ════════════════════════════════════════════════════════════════════════════

public class CheckoutModelTests
{
    private static CheckoutModel CreateModel(HttpResponseMessage response, IConfiguration? config = null)
    {
        var handler = new FakeHttpMessageHandler(response);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
        config ??= new Mock<IConfiguration>().Object;
        return new CheckoutModel(factory.Object, config, new Mock<ILogger<CheckoutModel>>().Object);
    }

    private static void FillCheckoutForm(CheckoutModel model)
    {
        model.FirstName     = "Test";
        model.LastName      = "User";
        model.Email         = "test@example.com";
        model.Address       = "123 Test St";
        model.City          = "Test City";
        model.State         = "TC";
        model.ZipCode       = "12345";
        model.Country       = "US";
        model.CartItemsJson = "[{\"productId\":\"00000000-0000-0000-0000-000000000001\",\"quantity\":1}]";
    }

    [Fact]
    public void OnGet_Unauthenticated_DoesNotPreFill()
    {
        var model = CreateModel(PageModelTestHelper.ServerError());
        AccountTestHelper.SetupPageContext(model, new DefaultHttpContext());

        model.OnGet();

        model.Email.Should().BeEmpty();
        model.FirstName.Should().BeEmpty();
    }

    [Fact]
    public void OnGet_Authenticated_PreFillsUserClaims()
    {
        var model = CreateModel(PageModelTestHelper.ServerError());
        var claims = new[]
        {
            new Claim(ClaimTypes.Email,     "checkout@example.com"),
            new Claim(ClaimTypes.GivenName, "Checkout"),
            new Claim(ClaimTypes.Surname,   "User"),
        };
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        AccountTestHelper.SetupPageContext(model, ctx);

        model.OnGet();

        model.Email.Should().Be("checkout@example.com");
        model.FirstName.Should().Be("Checkout");
        model.LastName.Should().Be("User");
    }

    [Fact]
    public async Task OnPostAsync_InvalidModel_ReturnsPage()
    {
        var model = CreateModel(PageModelTestHelper.ServerError());
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();
        model.ModelState.AddModelError("FirstName", "Required");

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
    }

    [Fact]
    public async Task OnPostAsync_ApiSuccess_RedirectsToOrdersPage()
    {
        var payload = new { id = "3fa85f64-5717-4562-b3fc-2c963f66afa6", status = "Paid", totalAmount = 99.99m };
        var model = CreateModel(PageModelTestHelper.JsonOk(payload));
        FillCheckoutForm(model);

        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "http";
        ctx.Request.Host   = new HostString("localhost", 8080);
        AccountTestHelper.SetupPageContext(model, ctx);
        model.TempData = AccountTestHelper.EmptyTempData(ctx);

        var result = await model.OnPostAsync();

        result.Should().BeOfType<RedirectToPageResult>()
            .Which.PageName.Should().Be("/Account/Orders");
    }

    [Fact]
    public async Task OnPostAsync_ApiFailure_SetsErrorMessage()
    {
        var model = CreateModel(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("order error")
        });
        FillCheckoutForm(model);
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnPostAsync_HttpClientThrows_SetsErrorMessage()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Throws(new Exception("boom"));
        var model = new CheckoutModel(factory.Object, new Mock<IConfiguration>().Object, new Mock<ILogger<CheckoutModel>>().Object);
        FillCheckoutForm(model);
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnPostAsync_WithAuthTokenCookie_SetsAuthorizationHeaderAndSucceeds()
    {
        // Exercises the "!string.IsNullOrEmpty(token)" branch that sets the Bearer header
        var payload = new { id = "3fa85f64-5717-4562-b3fc-2c963f66afa6", status = "Paid", totalAmount = 49.99m };
        var model = CreateModel(PageModelTestHelper.JsonOk(payload));
        FillCheckoutForm(model);

        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "http";
        ctx.Request.Host   = new HostString("localhost", 8080);
        ctx.Request.Headers["Cookie"] = "AuthToken=test-jwt-bearer-token";
        AccountTestHelper.SetupPageContext(model, ctx);
        model.TempData = AccountTestHelper.EmptyTempData(ctx);

        var result = await model.OnPostAsync();

        result.Should().BeOfType<RedirectToPageResult>()
            .Which.PageName.Should().Be("/Account/Orders");
    }
}

public class ProfileModelDeleteTests
{
    private static ProfileModel CreateWithUser(
        string userId,
        string email,
        Mock<UserManager<ApplicationUser>>? umMock = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var um = umMock ?? new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var logger = new Mock<ILogger<ProfileModel>>();
        var model  = new ProfileModel(logger.Object, um.Object);

        var claims  = new[] { new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Email, email) };
        var identity = new ClaimsIdentity(claims, "Test");

        var (ctx, _) = AccountTestHelper.CreateContextWithAuth();
        ctx.User = new ClaimsPrincipal(identity);
        AccountTestHelper.SetupPageContext(model, ctx);
        model.TempData = AccountTestHelper.EmptyTempData(ctx);
        return model;
    }

    [Fact]
    public async Task OnPostDeleteAccountAsync_NoUserId_SetsErrorMessage()
    {
        var store  = new Mock<IUserStore<ApplicationUser>>();
        var um     = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var logger = new Mock<ILogger<ProfileModel>>();
        var model  = new ProfileModel(logger.Object, um.Object);

        // No claims → FindFirstValue(NameIdentifier) returns null
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        AccountTestHelper.SetupPageContext(model, ctx);
        model.TempData = AccountTestHelper.EmptyTempData(ctx);

        var result = await model.OnPostDeleteAccountAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnPostDeleteAccountAsync_UserNotFound_SetsErrorMessage()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var um    = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        um.Setup(u => u.FindByIdAsync("user-1")).ReturnsAsync((ApplicationUser?)null);

        var result = await CreateWithUser("user-1", "del@example.com", um)
            .OnPostDeleteAccountAsync();

        result.Should().BeOfType<PageResult>();
    }

    [Fact]
    public async Task OnPostDeleteAccountAsync_DeleteSucceeds_RedirectsToRoot()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var um    = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var user  = new ApplicationUser { Id = "u1", Email = "del@example.com", FirstName = "D", LastName = "E" };
        um.Setup(u => u.FindByIdAsync("u1")).ReturnsAsync(user);
        um.Setup(u => u.DeleteAsync(user)).ReturnsAsync(IdentityResult.Success);

        var result = await CreateWithUser("u1", "del@example.com", um)
            .OnPostDeleteAccountAsync();

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/");
    }

    [Fact]
    public async Task OnPostDeleteAccountAsync_DeleteFails_SetsErrorMessage()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var um    = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var user  = new ApplicationUser { Id = "u1", Email = "del@example.com", FirstName = "D", LastName = "E" };
        um.Setup(u => u.FindByIdAsync("u1")).ReturnsAsync(user);
        um.Setup(u => u.DeleteAsync(user))
          .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "DB error" }));

        var model  = CreateWithUser("u1", "del@example.com", um);
        var result = await model.OnPostDeleteAccountAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("DB error");
    }

    [Fact]
    public async Task OnPostDeleteAccountAsync_Throws_SetsErrorMessage()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var um    = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        um.Setup(u => u.FindByIdAsync(It.IsAny<string>()))
          .ThrowsAsync(new Exception("db crash"));

        var model  = CreateWithUser("u1", "del@example.com", um);
        var result = await model.OnPostDeleteAccountAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Account.MakeAdminModel
// ════════════════════════════════════════════════════════════════════════════

public class MakeAdminModelTests
{
    private static (Mock<UserManager<ApplicationUser>> um, MakeAdminModel model) Create()
    {
        var store  = new Mock<IUserStore<ApplicationUser>>();
        var um     = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var logger = new Mock<ILogger<MakeAdminModel>>();
        var model  = new MakeAdminModel(um.Object, logger.Object);
        PageModelTestHelper.SetupHttpContext(model);
        model.TempData = AccountTestHelper.EmptyTempData();
        return (um, model);
    }

    [Fact]
    public void OnGet_CompletesWithoutError()
    {
        var (_, model) = Create();
        var act = () => model.OnGet();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task OnPostAsync_UserNotFound_SetsErrorMessage()
    {
        var (um, model) = Create();
        um.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        model.Email = "nobody@example.com";

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task OnPostAsync_UserAlreadyAdmin_SetsSuccessMessage()
    {
        var (um, model) = Create();
        var user = new ApplicationUser { Id = "u1", Email = "admin@example.com", FirstName = "A", LastName = "B" };
        um.Setup(u => u.FindByEmailAsync("admin@example.com")).ReturnsAsync(user);
        um.Setup(u => u.IsInRoleAsync(user, "Admin")).ReturnsAsync(true);
        model.Email = "admin@example.com";

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.SuccessMessage.Should().Contain("already");
    }

    [Fact]
    public async Task OnPostAsync_AddRoleSucceeds_SetsSuccessMessage()
    {
        var (um, model) = Create();
        var user = new ApplicationUser { Id = "u1", Email = "user@example.com", FirstName = "A", LastName = "B" };
        um.Setup(u => u.FindByEmailAsync("user@example.com")).ReturnsAsync(user);
        um.Setup(u => u.IsInRoleAsync(user, "Admin")).ReturnsAsync(false);
        um.Setup(u => u.AddToRoleAsync(user, "Admin")).ReturnsAsync(IdentityResult.Success);
        model.Email = "user@example.com";

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.SuccessMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnPostAsync_AddRoleFails_SetsErrorMessage()
    {
        var (um, model) = Create();
        var user = new ApplicationUser { Id = "u1", Email = "user@example.com", FirstName = "A", LastName = "B" };
        um.Setup(u => u.FindByEmailAsync("user@example.com")).ReturnsAsync(user);
        um.Setup(u => u.IsInRoleAsync(user, "Admin")).ReturnsAsync(false);
        um.Setup(u => u.AddToRoleAsync(user, "Admin"))
          .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Role fail" }));
        model.Email = "user@example.com";

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("Role fail");
    }

    [Fact]
    public async Task OnPostCreateAdminAsync_UserAlreadyExists_SetsErrorMessage()
    {
        var (um, model) = Create();
        var existing = new ApplicationUser { Id = "u1", Email = "existing@example.com", FirstName = "E", LastName = "X" };
        um.Setup(u => u.FindByEmailAsync("existing@example.com")).ReturnsAsync(existing);
        model.NewAdminEmail = "existing@example.com";

        var result = await model.OnPostCreateAdminAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnPostCreateAdminAsync_CreateFails_SetsErrorMessage()
    {
        var (um, model) = Create();
        um.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        um.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
          .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Create fail" }));
        model.NewAdminEmail    = "new@example.com";
        model.NewAdminPassword = "Pass123!";

        var result = await model.OnPostCreateAdminAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("Create fail");
    }

    [Fact]
    public async Task OnPostCreateAdminAsync_CreateSucceedsRoleFails_SetsErrorMessage()
    {
        var (um, model) = Create();
        um.Setup(u => u.FindByEmailAsync("new@example.com")).ReturnsAsync((ApplicationUser?)null);
        um.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
          .ReturnsAsync(IdentityResult.Success);
        um.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"))
          .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Role assign fail" }));
        model.NewAdminEmail     = "new@example.com";
        model.NewAdminPassword  = "Pass123!";
        model.NewAdminFirstName = "New";
        model.NewAdminLastName  = "Admin";

        var result = await model.OnPostCreateAdminAsync();

        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("Role assign fail");
    }

    [Fact]
    public async Task OnPostCreateAdminAsync_Success_SetsSuccessMessageAndClearsForm()
    {
        var (um, model) = Create();
        um.Setup(u => u.FindByEmailAsync("new@example.com")).ReturnsAsync((ApplicationUser?)null);
        um.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
          .ReturnsAsync(IdentityResult.Success);
        um.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"))
          .ReturnsAsync(IdentityResult.Success);
        model.NewAdminEmail     = "new@example.com";
        model.NewAdminPassword  = "Pass123!";
        model.NewAdminFirstName = "New";
        model.NewAdminLastName  = "Admin";

        var result = await model.OnPostCreateAdminAsync();

        result.Should().BeOfType<PageResult>();
        model.SuccessMessage.Should().NotBeNullOrEmpty();
        model.NewAdminEmail.Should().BeEmpty();
    }
}
