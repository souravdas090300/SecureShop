using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SecureShop.API.Services;
using SecureShop.Application.DTOs.Orders;
using SecureShop.Application.Interfaces;
using SecureShop.Application.Services;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Enums;
using SecureShop.Infrastructure;

namespace SecureShop.IntegrationTests;

/// <summary>
/// End-to-end payment lifecycle tests against real PostgreSQL: webhook
/// idempotency, atomic order transitions, exactly-once stock restore, late
/// payment refunds, and the abandonment sweep. Requires
/// SECURESHOP_TEST_DATABASE_URL; fails in CI when it is missing.
/// </summary>
public class StripePaymentLifecycleTests
{
    private readonly string? _connectionString =
        Environment.GetEnvironmentVariable("SECURESHOP_TEST_DATABASE_URL");

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Secret"] = "integration-test-secret-that-is-long-enough",
                    ["Jwt:Issuer"] = "SecureShop.Tests",
                    ["Jwt:Audience"] = "SecureShop.Tests"
                }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<FakePaymentService>();
                services.AddScoped<IPaymentService>(sp => sp.GetRequiredService<FakePaymentService>());
            });
        });

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

    /// <summary>Prepares the factory: starts the host, waits for migrations, returns it ready for use.</summary>
    private async Task<WebApplicationFactory<Program>> StartFactoryAsync()
    {
        RequireConnectionString();
        var previous = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _connectionString);
        WebApplicationFactory<Program> factory;
        try
        {
            factory = CreateFactory();
            _ = factory.CreateClient();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", previous);
        }
        await WaitForMigrationsAsync(factory.Services);
        return factory;
    }

    private void RequireConnectionString()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            if (Environment.GetEnvironmentVariable("CI") == "true")
                throw new InvalidOperationException(
                    "SECURESHOP_TEST_DATABASE_URL is not set; payment lifecycle tests cannot be skipped in CI.");
            throw new InvalidOperationException("SECURESHOP_TEST_DATABASE_URL is not set.");
        }

        // Refuse production-looking targets before the host (and its startup
        // migrations) ever connects.
        SecureShop.Infrastructure.TestDatabaseGuard.EnsureSafeTestConnectionString(_connectionString);
    }

    private static async Task<(Guid ProductId, string UserId)> SeedCatalogAsync(
        WebApplicationFactory<Program> factory, int stock = 10)
    {
        var userId = $"lifecycle-{Guid.NewGuid():N}";
        var product = Product.Create("Lifecycle Widget", "desc", 10.00m, stock, "Test");
        using var scope = factory.Services.CreateScope();
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
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return (product.Id, userId);
    }

    private static async Task<(Guid OrderId, string IntentId, long AmountCents)> CreateOrderAsync(
        WebApplicationFactory<Program> factory, Guid productId, string userId, int quantity = 2)
    {
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<OrderService>();
        var dto = await svc.CreateAsync(
            new CreateOrderDto([new OrderItemDto(productId, quantity)]), userId);
        return (dto.Id, dto.StripePaymentIntentId!,
            (long)decimal.Round(dto.TotalAmount * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static async Task ProcessEventAsync(
        WebApplicationFactory<Program> factory,
        string eventId, string eventType, string intentId,
        long amount = 0, long amountReceived = 0, string currency = "usd", string? failureCode = null)
    {
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<OrderService>();
        await svc.ProcessStripeEventAsync(eventId, eventType, intentId, amount, amountReceived, currency, failureCode);
    }

    private static async Task<int> GetStockAsync(WebApplicationFactory<Program> factory, Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync();
    }

    private static async Task<(OrderStatus Status, string? FailureCode, string? RefundId)> GetOrderStateAsync(
        WebApplicationFactory<Program> factory, Guid orderId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Orders
            .Where(o => o.Id == orderId)
            .Select(o => new { o.Status, o.LastPaymentFailureCode, o.StripeRefundId })
            .SingleAsync();
        return (row.Status, row.LastPaymentFailureCode, row.StripeRefundId);
    }

    private static async Task BackdateOrderAsync(WebApplicationFactory<Program> factory, Guid orderId, TimeSpan age)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var old = DateTime.UtcNow - age;
        await db.Database.ExecuteSqlAsync(
            $"UPDATE \"Orders\" SET \"CreatedAt\" = {old} WHERE \"Id\" = {orderId}");
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CanceledEvent_DeliveredTwice_RestoresStockExactlyOnce()
    {
        await using var factory = await StartFactoryAsync();
        var (productId, userId) = await SeedCatalogAsync(factory, stock: 10);
        var (orderId, intentId, _) = await CreateOrderAsync(factory, productId, userId, quantity: 2);
        Assert.Equal(8, await GetStockAsync(factory, productId));

        var evtId = $"evt_{Guid.NewGuid():N}";
        await ProcessEventAsync(factory, evtId, "payment_intent.canceled", intentId);
        await ProcessEventAsync(factory, evtId, "payment_intent.canceled", intentId); // duplicate

        var state = await GetOrderStateAsync(factory, orderId);
        Assert.Equal(OrderStatus.Cancelled, state.Status);
        Assert.Equal(10, await GetStockAsync(factory, productId)); // restored once, not twice
    }

    [Fact]
    public async Task CanceledEvent_ParallelDeliveries_RestoreStockExactlyOnce()
    {
        await using var factory = await StartFactoryAsync();
        var (productId, userId) = await SeedCatalogAsync(factory, stock: 10);
        var (orderId, intentId, _) = await CreateOrderAsync(factory, productId, userId, quantity: 2);

        var evtId = $"evt_{Guid.NewGuid():N}";
        await Task.WhenAll(
            ProcessEventAsync(factory, evtId, "payment_intent.canceled", intentId),
            ProcessEventAsync(factory, evtId, "payment_intent.canceled", intentId));

        var state = await GetOrderStateAsync(factory, orderId);
        Assert.Equal(OrderStatus.Cancelled, state.Status);
        Assert.Equal(10, await GetStockAsync(factory, productId));
    }

    [Fact]
    public async Task PaymentFailed_KeepsReservationAndRecordsFailure()
    {
        await using var factory = await StartFactoryAsync();
        var (productId, userId) = await SeedCatalogAsync(factory, stock: 10);
        var (orderId, intentId, _) = await CreateOrderAsync(factory, productId, userId, quantity: 2);

        await ProcessEventAsync(factory, $"evt_{Guid.NewGuid():N}", "payment_intent.payment_failed", intentId,
            failureCode: "card_declined");

        var state = await GetOrderStateAsync(factory, orderId);
        Assert.Equal(OrderStatus.PaymentProcessing, state.Status);
        Assert.Equal("card_declined", state.FailureCode);
        Assert.Equal(8, await GetStockAsync(factory, productId)); // reservation kept
    }

    [Fact]
    public async Task CanceledEvent_ForPaidOrder_IsIgnored()
    {
        await using var factory = await StartFactoryAsync();
        var (productId, userId) = await SeedCatalogAsync(factory, stock: 10);
        var (orderId, intentId, amountCents) = await CreateOrderAsync(factory, productId, userId, quantity: 2);

        await ProcessEventAsync(factory, $"evt_{Guid.NewGuid():N}", "payment_intent.succeeded", intentId, amountCents, amountCents);
        await ProcessEventAsync(factory, $"evt_{Guid.NewGuid():N}", "payment_intent.canceled", intentId);

        var state = await GetOrderStateAsync(factory, orderId);
        Assert.Equal(OrderStatus.Paid, state.Status);
        Assert.Equal(8, await GetStockAsync(factory, productId)); // stock stays reserved for fulfilment
    }

    [Fact]
    public async Task SucceededEvent_ForCancelledOrder_RefundsLatePayment()
    {
        await using var factory = await StartFactoryAsync();
        var (productId, userId) = await SeedCatalogAsync(factory, stock: 10);
        var (orderId, intentId, amountCents) = await CreateOrderAsync(factory, productId, userId, quantity: 2);

        await ProcessEventAsync(factory, $"evt_{Guid.NewGuid():N}", "payment_intent.canceled", intentId);
        await ProcessEventAsync(factory, $"evt_{Guid.NewGuid():N}", "payment_intent.succeeded", intentId, amountCents, amountCents);

        var state = await GetOrderStateAsync(factory, orderId);
        Assert.Equal(OrderStatus.Cancelled, state.Status);   // never flipped to Paid
        Assert.NotNull(state.RefundId);                       // refund recorded on the order
        Assert.Equal(10, await GetStockAsync(factory, productId));

        var fake = factory.Services.GetRequiredService<FakePaymentService>();
        Assert.Contains(fake.Refunds, r => r.IntentId == intentId);
    }

    [Fact]
    public async Task PaymentFailed_AfterSucceeded_DoesNotTouchPaidOrder()
    {
        await using var factory = await StartFactoryAsync();
        var (productId, userId) = await SeedCatalogAsync(factory, stock: 10);
        var (orderId, intentId, amountCents) = await CreateOrderAsync(factory, productId, userId, quantity: 2);

        await ProcessEventAsync(factory, $"evt_{Guid.NewGuid():N}", "payment_intent.succeeded", intentId, amountCents, amountCents);
        // Out-of-order delivery: the failure event arrives after the success.
        await ProcessEventAsync(factory, $"evt_{Guid.NewGuid():N}", "payment_intent.payment_failed", intentId,
            failureCode: "card_declined");

        var state = await GetOrderStateAsync(factory, orderId);
        Assert.Equal(OrderStatus.Paid, state.Status);
        Assert.Null(state.FailureCode);
        Assert.Equal(8, await GetStockAsync(factory, productId));
    }

    [Fact]
    public async Task SucceededEvent_RefundFailure_RollsBackEventClaimForRetry()
    {
        await using var factory = await StartFactoryAsync();
        var (productId, userId) = await SeedCatalogAsync(factory, stock: 10);
        var (orderId, intentId, amountCents) = await CreateOrderAsync(factory, productId, userId, quantity: 2);

        await ProcessEventAsync(factory, $"evt_{Guid.NewGuid():N}", "payment_intent.canceled", intentId);

        var fake = factory.Services.GetRequiredService<FakePaymentService>();
        fake.FailingRefunds.Add(intentId);

        var evtId = $"evt_{Guid.NewGuid():N}";
        // The refund throws inside the event transaction → the whole unit
        // (including the ProcessedStripeEvents claim) rolls back and the
        // exception propagates so the endpoint returns 5xx and Stripe retries.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProcessEventAsync(factory, evtId, "payment_intent.succeeded", intentId, amountCents, amountCents));

        Assert.False(await EventRecordedAsync(factory, evtId), "failed event must not be recorded as processed");
        var state = await GetOrderStateAsync(factory, orderId);
        Assert.Equal(OrderStatus.Cancelled, state.Status);
        Assert.Null(state.RefundId);

        // Stripe redelivers the same event once the refund path recovers —
        // the idempotency key makes the retry safe.
        fake.FailingRefunds.Remove(intentId);
        await ProcessEventAsync(factory, evtId, "payment_intent.succeeded", intentId, amountCents, amountCents);

        state = await GetOrderStateAsync(factory, orderId);
        Assert.Equal(OrderStatus.Cancelled, state.Status);
        Assert.NotNull(state.RefundId);
        Assert.True(await EventRecordedAsync(factory, evtId));
    }

    private static async Task<bool> EventRecordedAsync(WebApplicationFactory<Program> factory, string eventId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProcessedStripeEvents.AnyAsync(e => e.EventId == eventId);
    }

    [Fact]
    public async Task SucceededEvent_MarksPaymentProcessingOrderPaid()
    {
        await using var factory = await StartFactoryAsync();
        var (productId, userId) = await SeedCatalogAsync(factory, stock: 10);
        var (orderId, intentId, amountCents) = await CreateOrderAsync(factory, productId, userId, quantity: 2);

        var evtId = $"evt_{Guid.NewGuid():N}";
        await ProcessEventAsync(factory, evtId, "payment_intent.succeeded", intentId, amountCents, amountCents);
        // Stripe often delivers the same event twice — second delivery must be a no-op.
        await ProcessEventAsync(factory, evtId, "payment_intent.succeeded", intentId, amountCents, amountCents);

        var state = await GetOrderStateAsync(factory, orderId);
        Assert.Equal(OrderStatus.Paid, state.Status);
        Assert.Equal(8, await GetStockAsync(factory, productId));
    }

    [Fact]
    public async Task AbandonmentSweep_CancelsOnlyStaleUnpaidOrders()
    {
        await using var factory = await StartFactoryAsync();
        var (productId, userId) = await SeedCatalogAsync(factory, stock: 10);

        // staleCancel: unpaid past the cutoff → job cancels intent + order
        var staleCancel = await CreateOrderAsync(factory, productId, userId, quantity: 1);
        await BackdateOrderAsync(factory, staleCancel.OrderId, TimeSpan.FromHours(2));

        // fresh: created just now → skipped
        var fresh = await CreateOrderAsync(factory, productId, userId, quantity: 1);

        // staleSucceeded: intent already succeeded at Stripe → skipped, webhook marks Paid
        var staleSucceeded = await CreateOrderAsync(factory, productId, userId, quantity: 1);
        await BackdateOrderAsync(factory, staleSucceeded.OrderId, TimeSpan.FromHours(2));
        factory.Services.GetRequiredService<FakePaymentService>()
            .SetIntentState(staleSucceeded.IntentId, PaymentIntentState.Succeeded);

        // staleStripeCanceled: Stripe side already canceled → order reconciled without a Stripe cancel call
        var staleStripeCanceled = await CreateOrderAsync(factory, productId, userId, quantity: 1);
        await BackdateOrderAsync(factory, staleStripeCanceled.OrderId, TimeSpan.FromHours(2));
        factory.Services.GetRequiredService<FakePaymentService>()
            .SetIntentState(staleStripeCanceled.IntentId, PaymentIntentState.Canceled);

        // staleProcessing: intent mid-flight at Stripe — cannot be cancelled,
        // so the sweep must skip it rather than erroring every interval
        var staleProcessing = await CreateOrderAsync(factory, productId, userId, quantity: 1);
        await BackdateOrderAsync(factory, staleProcessing.OrderId, TimeSpan.FromHours(2));
        factory.Services.GetRequiredService<FakePaymentService>()
            .SetIntentState(staleProcessing.IntentId, PaymentIntentState.Processing);

        // staleFailing: Stripe status lookup throws → left alone for next sweep
        var staleFailing = await CreateOrderAsync(factory, productId, userId, quantity: 1);
        await BackdateOrderAsync(factory, staleFailing.OrderId, TimeSpan.FromHours(2));
        factory.Services.GetRequiredService<FakePaymentService>()
            .FailingIntents.Add(staleFailing.IntentId);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orders:AbandonAfterMinutes"] = "30",
                ["Orders:AbandonmentIntervalMinutes"] = "5"
            })
            .Build();
        var job = new OrderAbandonmentService(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            config,
            NullLogger<OrderAbandonmentService>.Instance);

        await job.SweepOnceAsync();

        Assert.Equal(OrderStatus.Cancelled, (await GetOrderStateAsync(factory, staleCancel.OrderId)).Status);
        Assert.Equal(OrderStatus.PaymentProcessing, (await GetOrderStateAsync(factory, fresh.OrderId)).Status);
        Assert.Equal(OrderStatus.PaymentProcessing, (await GetOrderStateAsync(factory, staleSucceeded.OrderId)).Status);
        Assert.Equal(OrderStatus.PaymentProcessing, (await GetOrderStateAsync(factory, staleProcessing.OrderId)).Status);
        Assert.Equal(OrderStatus.Cancelled, (await GetOrderStateAsync(factory, staleStripeCanceled.OrderId)).Status);
        Assert.Equal(OrderStatus.PaymentProcessing, (await GetOrderStateAsync(factory, staleFailing.OrderId)).Status);

        // Stock: 10 - 6 reserved = 4; staleCancel + staleStripeCanceled restored → 6.
        Assert.Equal(6, await GetStockAsync(factory, productId));

        var fake = factory.Services.GetRequiredService<FakePaymentService>();
        Assert.Contains(staleCancel.IntentId, fake.CancelledIntents);
        Assert.DoesNotContain(staleStripeCanceled.IntentId, fake.CancelledIntents); // already canceled at Stripe
        Assert.DoesNotContain(staleSucceeded.IntentId, fake.CancelledIntents);
        Assert.DoesNotContain(staleProcessing.IntentId, fake.CancelledIntents);
        Assert.DoesNotContain(staleFailing.IntentId, fake.CancelledIntents);
    }
}
