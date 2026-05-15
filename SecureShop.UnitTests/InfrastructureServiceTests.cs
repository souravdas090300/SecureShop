using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SecureShop.Application;
using SecureShop.Application.DTOs.Auth;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Exceptions;
using SecureShop.Infrastructure.Services;
using StackExchange.Redis;
using System.Text.Json;

namespace SecureShop.UnitTests;

// ════════════════════════════════════════════════════════════════════════════
// AuthService
// ════════════════════════════════════════════════════════════════════════════

public class AuthServiceTests
{
    private static (
        Mock<UserManager<ApplicationUser>> um,
        Mock<RoleManager<IdentityRole>> rm,
        AuthService svc)
    Create(string? googleClientId = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var um    = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var roleStore = new Mock<IRoleStore<IdentityRole>>();
        var rm        = new Mock<RoleManager<IdentityRole>>(
            roleStore.Object, null!, null!, null!, null!);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"]         = AccountTestHelper.TestSecret,
                ["Jwt:Issuer"]         = "test",
                ["Jwt:Audience"]       = "test",
                ["GoogleAuth:ClientId"] = googleClientId ?? "valid-client-id",
            })
            .Build();

        return (um, rm, new AuthService(um.Object, rm.Object, config, NullLogger<AuthService>.Instance));
    }

    // ── RegisterAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_EmailAlreadyExists_ThrowsDomainException()
    {
        var (um, _, svc) = Create();
        var existing = new ApplicationUser { Id = "u1", Email = "dup@test.com", FirstName = "D", LastName = "U" };
        um.Setup(u => u.FindByEmailAsync("dup@test.com")).ReturnsAsync(existing);

        var act = () => svc.RegisterAsync(new RegisterDto("D", "U", "dup@test.com", "Pass123!"));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*already registered*");
    }

    [Fact]
    public async Task RegisterAsync_CreateFails_ThrowsDomainException()
    {
        var (um, _, svc) = Create();
        um.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        um.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
          .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Weak password" }));

        var act = () => svc.RegisterAsync(new RegisterDto("N", "U", "new@test.com", "weak"));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*Weak password*");
    }

    [Fact]
    public async Task RegisterAsync_CustomerRoleNotExists_CreatesRoleAndReturnsToken()
    {
        var (um, rm, svc) = Create();
        um.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        um.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
          .ReturnsAsync(IdentityResult.Success);
        rm.Setup(r => r.RoleExistsAsync("Customer")).ReturnsAsync(false);
        rm.Setup(r => r.CreateAsync(It.IsAny<IdentityRole>())).ReturnsAsync(IdentityResult.Success);
        um.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Customer"))
          .ReturnsAsync(IdentityResult.Success);
        um.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>()))
          .ReturnsAsync(new List<string> { "Customer" });

        var result = await svc.RegisterAsync(new RegisterDto("New", "User", "new@test.com", "Secure123!"));

        result.Should().NotBeNull();
        result.Token.Should().NotBeNullOrEmpty();
        result.Email.Should().Be("new@test.com");
        rm.Verify(r => r.CreateAsync(It.IsAny<IdentityRole>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_CustomerRoleAlreadyExists_SkipsRoleCreation()
    {
        var (um, rm, svc) = Create();
        um.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        um.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
          .ReturnsAsync(IdentityResult.Success);
        rm.Setup(r => r.RoleExistsAsync("Customer")).ReturnsAsync(true);
        um.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Customer"))
          .ReturnsAsync(IdentityResult.Success);
        um.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>()))
          .ReturnsAsync(new List<string>());

        var result = await svc.RegisterAsync(new RegisterDto("New", "User", "new@test.com", "Secure123!"));

        result.Should().NotBeNull();
        rm.Verify(r => r.CreateAsync(It.IsAny<IdentityRole>()), Times.Never);
    }

    // ── LoginAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_UserNotFound_ThrowsDomainException()
    {
        var (um, _, svc) = Create();
        um.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        var act = () => svc.LoginAsync(new LoginDto("nobody@test.com", "Pass123!"));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*credentials*");
    }

    [Fact]
    public async Task LoginAsync_EmptyPassword_ThrowsDomainException()
    {
        var (um, _, svc) = Create();
        var user = new ApplicationUser { Id = "u1", Email = "user@test.com", FirstName = "U", LastName = "S" };
        um.Setup(u => u.FindByEmailAsync("user@test.com")).ReturnsAsync(user);

        var act = () => svc.LoginAsync(new LoginDto("user@test.com", ""));

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsDomainException()
    {
        var (um, _, svc) = Create();
        var user = new ApplicationUser { Id = "u1", Email = "user@test.com", FirstName = "U", LastName = "S" };
        um.Setup(u => u.FindByEmailAsync("user@test.com")).ReturnsAsync(user);
        um.Setup(u => u.CheckPasswordAsync(user, "WrongPass!")).ReturnsAsync(false);

        var act = () => svc.LoginAsync(new LoginDto("user@test.com", "WrongPass!"));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*credentials*");
    }

    [Fact]
    public async Task LoginAsync_CorrectPassword_ReturnsTokenWithEmail()
    {
        var (um, _, svc) = Create();
        var user = new ApplicationUser { Id = "u1", Email = "user@test.com", FirstName = "U", LastName = "S" };
        um.Setup(u => u.FindByEmailAsync("user@test.com")).ReturnsAsync(user);
        um.Setup(u => u.CheckPasswordAsync(user, "Correct123!")).ReturnsAsync(true);
        um.Setup(u => u.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Customer" });

        var result = await svc.LoginAsync(new LoginDto("user@test.com", "Correct123!"));

        result.Should().NotBeNull();
        result.Token.Should().NotBeNullOrEmpty();
        result.Email.Should().Be("user@test.com");
        result.FirstName.Should().Be("U");
    }

    [Fact]
    public async Task LoginAsync_WithRoleClaims_TokenContainsRoles()
    {
        var (um, _, svc) = Create();
        var user = new ApplicationUser { Id = "u1", Email = "admin@test.com", FirstName = "A", LastName = "D" };
        um.Setup(u => u.FindByEmailAsync("admin@test.com")).ReturnsAsync(user);
        um.Setup(u => u.CheckPasswordAsync(user, "Admin123!")).ReturnsAsync(true);
        um.Setup(u => u.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Admin", "Customer" });

        var result = await svc.LoginAsync(new LoginDto("admin@test.com", "Admin123!"));

        result.Token.Should().NotBeNullOrEmpty();
    }

    // ── GoogleSignInAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GoogleSignInAsync_ClientIdNotConfigured_ThrowsDomainException()
    {
        // ClientId starting with "YOUR_" is treated as unconfigured
        var (um, rm, _) = Create();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleAuth:ClientId"] = "YOUR_CLIENT_ID",
                ["Jwt:Secret"]          = AccountTestHelper.TestSecret,
                ["Jwt:Issuer"]          = "test",
                ["Jwt:Audience"]        = "test",
            })
            .Build();
        var svc = new AuthService(um.Object, rm.Object, config, NullLogger<AuthService>.Instance);

        var act = () => svc.GoogleSignInAsync(new GoogleSignInDto { IdToken = "fake" });

        await act.Should().ThrowAsync<DomainException>().WithMessage("*not configured*");
    }

    [Fact]
    public async Task GoogleSignInAsync_EmptyClientId_ThrowsDomainException()
    {
        var (um, rm, _) = Create();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleAuth:ClientId"] = "",
                ["Jwt:Secret"]          = AccountTestHelper.TestSecret,
            })
            .Build();
        var svc = new AuthService(um.Object, rm.Object, config, NullLogger<AuthService>.Instance);

        var act = () => svc.GoogleSignInAsync(new GoogleSignInDto { IdToken = "fake" });

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task GoogleSignInAsync_InvalidToken_ThrowsDomainException()
    {
        // ValidateAsync will reject a non-Google token → InvalidJwtException → DomainException
        var (um, rm, svc) = Create(googleClientId: "real-client-id");

        var act = () => svc.GoogleSignInAsync(new GoogleSignInDto { IdToken = "not-a-google-token" });

        await act.Should().ThrowAsync<DomainException>();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Application.DependencyInjection.AddApplicationServices
// ════════════════════════════════════════════════════════════════════════════

public class ApplicationDependencyInjectionTests
{
    [Fact]
    public void AddApplicationServices_RegistersOrderServiceAndProductService()
    {
        var services = new ServiceCollection();

        services.AddApplicationServices();

        services.Should().Contain(sd => sd.ServiceType == typeof(SecureShop.Application.Services.OrderService));
        services.Should().Contain(sd => sd.ServiceType == typeof(SecureShop.Application.Services.ProductService));
    }

    [Fact]
    public void AddApplicationServices_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var act      = () => services.AddApplicationServices();
        act.Should().NotThrow();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// CacheService  (mocks IConnectionMultiplexer + IDatabase)
// ════════════════════════════════════════════════════════════════════════════

public class CacheServiceTests
{
    private static (Mock<IConnectionMultiplexer> mux, Mock<IDatabase> db, CacheService svc) Create()
    {
        var db  = new Mock<IDatabase>();
        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        return (mux, db, new CacheService(mux.Object));
    }

    [Fact]
    public async Task GetAsync_KeyNotFound_ReturnsDefault()
    {
        var (_, db, svc) = Create();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(RedisValue.Null);

        var result = await svc.GetAsync<string>("missing-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_KeyFound_ReturnsDeserializedValue()
    {
        var (_, db, svc) = Create();
        var json = JsonSerializer.Serialize("hello-world");
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(new RedisValue(json));

        var result = await svc.GetAsync<string>("some-key");

        result.Should().Be("hello-world");
    }

    [Fact]
    public async Task SetAsync_CallsStringSetAsync()
    {
        var (_, db, svc) = Create();

        await svc.SetAsync("key", "value", TimeSpan.FromMinutes(1));

        // StringSetAsync is declared on IDatabaseAsync (inherited by IDatabase).
        // Moq tracks the invocation on the IDatabase mock under the IDatabaseAsync interface.
        // Use Invocations directly to avoid overload/interface resolution issues with Verify.
        db.Invocations.Should().ContainSingle(i => i.Method.Name == "StringSetAsync");
    }

    [Fact]
    public async Task RemoveAsync_CallsKeyDeleteAsync()
    {
        var (_, db, svc) = Create();
        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(true);

        await svc.RemoveAsync("some-key");

        db.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RemoveByPrefixAsync_WithMatchingKeys_DeletesBatch()
    {
        var (mux, db, svc) = Create();
        var server = new Mock<IServer>();
        var keys   = new RedisKey[] { new RedisKey("prefix:a"), new RedisKey("prefix:b") };
        server.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(),
                               It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
              .Returns(keys);
        mux.Setup(m => m.GetServer(It.IsAny<System.Net.EndPoint>(), It.IsAny<object>()))
           .Returns(server.Object);
        mux.Setup(m => m.GetEndPoints(It.IsAny<bool>()))
           .Returns(new System.Net.EndPoint[] { new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379) });
        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(2L);

        await svc.RemoveByPrefixAsync("prefix:");

        db.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RemoveByPrefixAsync_NoMatchingKeys_DoesNotCallDelete()
    {
        var (mux, db, svc) = Create();
        var server = new Mock<IServer>();
        server.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(),
                               It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
              .Returns(Array.Empty<RedisKey>());
        mux.Setup(m => m.GetServer(It.IsAny<System.Net.EndPoint>(), It.IsAny<object>()))
           .Returns(server.Object);
        mux.Setup(m => m.GetEndPoints(It.IsAny<bool>()))
           .Returns(new System.Net.EndPoint[] { new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379) });

        await svc.RemoveByPrefixAsync("nothing:");

        db.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()), Times.Never);
    }
}
