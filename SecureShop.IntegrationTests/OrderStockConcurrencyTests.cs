using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecureShop.Application.DTOs.Orders;
using SecureShop.Application.Interfaces;
using SecureShop.Application.Services;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Exceptions;
using SecureShop.Infrastructure;

namespace SecureShop.IntegrationTests;

/// <summary>
/// Verifies that concurrent order creation cannot oversell stock.
/// Requires a real PostgreSQL database: EF Core's ExecuteUpdateAsync is not
/// supported by the InMemory provider, and SQLite semantics differ.
/// Set SECURESHOP_TEST_DATABASE_URL to a disposable test database connection
/// string; the test is skipped when it is not configured.
/// </summary>
// DB-backed classes share one collection so their WebApplicationFactory
// instances never migrate the same test database concurrently.
[Collection("SecureShopDatabase")]
public class OrderStockConcurrencyTests
{
    private readonly string? _connectionString =
        Environment.GetEnvironmentVariable("SECURESHOP_TEST_DATABASE_URL");

    [Fact]
    public async Task ConcurrentOrders_NeverOversell()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            // In CI a missing connection string must fail loudly — a silent
            // skip would let an overselling regression ship undetected.
            if (Environment.GetEnvironmentVariable("CI") == "true")
                throw new InvalidOperationException(
                    "SECURESHOP_TEST_DATABASE_URL is not set; the stock concurrency test cannot be skipped in CI.");
            return; // No test database configured — nothing to verify against.
        }

        // Refuse production-looking targets before the host (and its startup
        // migrations) ever connects.
        SecureShop.Infrastructure.TestDatabaseGuard.EnsureSafeTestConnectionString(_connectionString);

        // appsettings.json ships "DefaultConnection": "" which would win over the
        // in-memory override below, so set the environment variable instead —
        // environment variables are loaded after appsettings.json. The variable
        // is process-wide, so restore the previous value as soon as the host has
        // consumed it, to avoid leaking the test database into parallel tests.
        var previousConnectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _connectionString);

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Secret"] = "integration-test-secret-that-is-long-enough",
                    ["Jwt:Issuer"] = "SecureShop.Tests",
                    ["Jwt:Audience"] = "SecureShop.Tests",
                    ["ConnectionStrings:DefaultConnection"] = _connectionString
                }));
            builder.ConfigureServices(services =>
            {
                // Replace Stripe with a deterministic fake; stock reservation is
                // the behaviour under test, not payment processing.
                services.AddSingleton<FakePaymentService>();
                services.AddScoped<IPaymentService>(sp => sp.GetRequiredService<FakePaymentService>());
            });
        });

        // The app migrates in a background task after startup. Wait for it to
        // finish rather than calling MigrateAsync ourselves — two concurrent
        // migrators race on creating __EFMigrationsHistory.
        try
        {
            _ = factory.CreateClient(); // ensure the host (and its migration task) has started
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", previousConnectionString);
        }
        await WaitForMigrationsAsync(factory.Services);

        var userId = $"concurrency-{Guid.NewGuid():N}";
        Product product;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = userId,
                NormalizedUserName = userId.ToUpperInvariant(),
                Email = $"{userId}@test.com",
                NormalizedEmail = $"{userId}@test.com".ToUpperInvariant(),
                FirstName = "Test",
                LastName = "User",
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString()
            });
            product = Product.Create("Concurrency Widget", "desc", 1.00m, stock: 5, "Test");
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var productId = product.Id;
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            // A separate DI scope per task gives each order its own DbContext,
            // matching how concurrent HTTP requests are served.
            using var scope = factory.Services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<OrderService>();
            try
            {
                await svc.CreateAsync(
                    new CreateOrderDto([new OrderItemDto(productId, 1)]),
                    userId);
                return true;
            }
            catch (DomainException)
            {
                return false;
            }
        }));

        Assert.Equal(5, results.Count(success => success));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stock = await db.Products
                .Where(p => p.Id == productId)
                .Select(p => p.StockQuantity)
                .SingleAsync();
            Assert.Equal(0, stock);
        }
    }

    private static async Task WaitForMigrationsAsync(IServiceProvider services)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var pending = await db.Database.GetPendingMigrationsAsync();
                if (!pending.Any())
                    return;
            }
            catch
            {
                // History table not created yet or DB still waking up — retry.
            }
            await Task.Delay(1000);
        }
        throw new TimeoutException("Database migrations did not complete in time.");
    }

}
