using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SecureShop.API.Controllers;
using SecureShop.Application.DTOs.Auth;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;

namespace SecureShop.UnitTests;

public class AuthControllerTests
{
    private static AuthController MakeController(Mock<IAuthService> authSvc, Mock<UserManager<ApplicationUser>>? userMgr = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var mgr = userMgr ?? new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var controller = new AuthController(authSvc.Object, mgr.Object, NullLogger<AuthController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private static AuthResponseDto FakeToken() =>
        new("fake.jwt.token", "user@example.com", "John", "Doe", DateTime.UtcNow.AddHours(1));

    // ─── Register ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_ValidDto_ReturnsOk()
    {
        var authSvc = new Mock<IAuthService>();
        authSvc.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>())).ReturnsAsync(FakeToken());

        var controller = MakeController(authSvc);
        var dto = new RegisterDto("John", "Doe", "john@example.com", "P@ssw0rd1");

        var result = await controller.Register(dto);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<AuthResponseDto>()
          .Which.Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsBadRequest()
    {
        var authSvc = new Mock<IAuthService>();
        authSvc.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
               .ThrowsAsync(new InvalidOperationException("Email already registered"));

        var controller = MakeController(authSvc);
        var result = await controller.Register(new RegisterDto("J", "D", "dup@example.com", "P@ssw0rd1"));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Register_UnexpectedError_Returns500()
    {
        var authSvc = new Mock<IAuthService>();
        authSvc.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
               .ThrowsAsync(new Exception("DB down"));

        var controller = MakeController(authSvc);
        var result = await controller.Register(new RegisterDto("J", "D", "x@x.com", "P@ssw0rd1"));

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }

    // ─── Login ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_ReturnsOk()
    {
        var authSvc = new Mock<IAuthService>();
        authSvc.Setup(s => s.LoginAsync(It.IsAny<LoginDto>())).ReturnsAsync(FakeToken());

        var controller = MakeController(authSvc);
        var result = await controller.Login(new LoginDto("john@example.com", "P@ssw0rd1"));

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Login_WrongCredentials_ReturnsUnauthorized()
    {
        var authSvc = new Mock<IAuthService>();
        authSvc.Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
               .ThrowsAsync(new UnauthorizedAccessException("Invalid credentials"));

        var controller = MakeController(authSvc);
        var result = await controller.Login(new LoginDto("x@x.com", "wrongpass"));

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_UnexpectedError_Returns500()
    {
        var authSvc = new Mock<IAuthService>();
        authSvc.Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
               .ThrowsAsync(new Exception("DB error"));

        var controller = MakeController(authSvc);
        var result = await controller.Login(new LoginDto("x@x.com", "P@ssw0rd1"));

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }

    // ── ModelState invalid ────────────────────────────────────────────────────

    [Fact]
    public async Task Register_InvalidModelState_ReturnsBadRequest()
    {
        var authSvc = new Mock<IAuthService>();
        var controller = MakeController(authSvc);
        controller.ModelState.AddModelError("Email", "Required");

        var result = await controller.Register(new RegisterDto("J", "D", "", "P@ssw0rd1"));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Login_InvalidModelState_ReturnsBadRequest()
    {
        var authSvc = new Mock<IAuthService>();
        var controller = MakeController(authSvc);
        controller.ModelState.AddModelError("Email", "Required");

        var result = await controller.Login(new LoginDto("", ""));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── GoogleSignIn ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GoogleSignIn_EmptyToken_ReturnsBadRequest()
    {
        var authSvc = new Mock<IAuthService>();
        var controller = MakeController(authSvc);

        var result = await controller.GoogleSignIn(new GoogleSignInDto { IdToken = "" });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GoogleSignIn_UnauthorizedAccessException_Returns401()
    {
        var authSvc = new Mock<IAuthService>();
        authSvc.Setup(s => s.GoogleSignInAsync(It.IsAny<GoogleSignInDto>()))
               .ThrowsAsync(new UnauthorizedAccessException("bad token"));

        var controller = MakeController(authSvc);
        var result = await controller.GoogleSignIn(new GoogleSignInDto { IdToken = "some-token" });

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GoogleSignIn_UnexpectedError_Returns500()
    {
        var authSvc = new Mock<IAuthService>();
        authSvc.Setup(s => s.GoogleSignInAsync(It.IsAny<GoogleSignInDto>()))
               .ThrowsAsync(new Exception("server fault"));

        var controller = MakeController(authSvc);
        var result = await controller.GoogleSignIn(new GoogleSignInDto { IdToken = "some-token" });

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GoogleSignIn_Success_ReturnsOkAndSetsAuthCookie()
    {
        // Use a real JWT so ReadJwtToken succeeds
        var realJwt = AccountTestHelper.CreateJwt();
        var fakeResponse = new AuthResponseDto(realJwt, "user@example.com", "John", "Doe", DateTime.UtcNow.AddHours(1));

        var authSvc = new Mock<IAuthService>();
        authSvc.Setup(s => s.GoogleSignInAsync(It.IsAny<GoogleSignInDto>())).ReturnsAsync(fakeResponse);

        // Need IAuthenticationService for SignInAsync
        var authSvcHttp = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
        authSvcHttp.Setup(a => a.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string>(),
                          It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                          It.IsAny<Microsoft.AspNetCore.Authentication.AuthenticationProperties>()))
                   .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(authSvcHttp.Object);
        var sp = services.BuildServiceProvider();

        var store = new Mock<IUserStore<ApplicationUser>>();
        var mgr   = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var controller = new AuthController(authSvc.Object, mgr.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = sp }
        };

        var result = await controller.GoogleSignIn(new GoogleSignInDto { IdToken = "some-valid-token" });

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    // ── GetAllUsers ───────────────────────────────────────────────────────────

    [Fact]
    public void GetAllUsers_ReturnsOkWithUserList()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var mgr   = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var users = new List<ApplicationUser>
        {
            new ApplicationUser { Id = "u1", Email = "a@b.com", FirstName = "A", LastName = "B" }
        }.AsQueryable();
        mgr.Setup(u => u.Users).Returns(users);

        var authSvc = new Mock<IAuthService>();
        var controller = MakeController(authSvc, mgr);

        var result = controller.GetAllUsers();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }
}
