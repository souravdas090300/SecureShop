using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SecureShop.API.Controllers;
using SecureShop.Application.Interfaces;
using SecureShop.Application.Services;
using SecureShop.Domain.Entities;
using SecureShop.Infrastructure.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stripe;

namespace SecureShop.UnitTests;

public class StripeWebhookControllerTests
{
    [Fact]
    public async Task Receive_WhenWebhookSecretIsMissing_ReturnsServiceUnavailable()
    {
        var controller = CreateController(new ConfigurationBuilder().Build());

        var result = await controller.Receive(CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task Receive_WhenSignatureIsInvalid_ReturnsBadRequest()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:WebhookSecret"] = "whsec_test"
            })
            .Build();
        var controller = CreateController(configuration);
        controller.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"));
        controller.Request.Headers["Stripe-Signature"] = "invalid";

        var result = await controller.Receive(CancellationToken.None);

        result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public async Task Receive_WhenVerifiedPaymentMatchesOrder_MarksOrderPaid()
    {
        const string webhookSecret = "whsec_test_secret";
        const string paymentIntentId = "pi_test_123";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(new
        {
            id = "evt_test_123",
            @object = "event",
            api_version = "2026-02-25.clover",
            created = timestamp,
            data = new
            {
                @object = new
                {
                    id = paymentIntentId,
                    @object = "payment_intent",
                    amount = 999,
                    amount_received = 999,
                    currency = "usd",
                    status = "succeeded"
                }
            },
            livemode = false,
            pending_webhooks = 1,
            request = new { id = (string?)null, idempotency_key = (string?)null },
            type = "payment_intent.succeeded"
        });
        var signature = CreateSignature(payload, webhookSecret, timestamp);
        var parsedEvent = EventUtility.ConstructEvent(payload, signature, webhookSecret);
        parsedEvent.Type.Should().Be("payment_intent.succeeded");
        parsedEvent.Data.Object.Should().BeOfType<PaymentIntent>();
        var order = Order.Create("user-1", [OrderItem.Create(Guid.NewGuid(), 1, 9.99m)]);
        order.SetPaymentIntent(paymentIntentId);
        var orderRepository = new Mock<IOrderRepository>();
        orderRepository.Setup(r => r.GetByPaymentIntentIdAsync(
                paymentIntentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        orderRepository.Setup(r => r.TryTransitionStatusAsync(
                order.Id, SecureShop.Domain.Enums.OrderStatus.PaymentProcessing,
                SecureShop.Domain.Enums.OrderStatus.Paid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<Task<bool>> work, CancellationToken ct) => work());
        var stripeEvents = new Mock<IStripeEventRepository>();
        stripeEvents.Setup(r => r.TryRecordProcessedEventAsync(
                "evt_test_123", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:WebhookSecret"] = webhookSecret
            })
            .Build();
        var controller = CreateController(configuration, orderRepository, unitOfWork, stripeEvents);
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        controller.Request.Headers["Stripe-Signature"] = signature;

        var result = await controller.Receive(CancellationToken.None);

        result.Should().BeOfType<OkResult>();
        // The Paid transition is an atomic conditional UPDATE — the tracked
        // entity is not mutated, so assert on the repository call itself.
        orderRepository.Verify(r => r.TryTransitionStatusAsync(
            order.Id, SecureShop.Domain.Enums.OrderStatus.PaymentProcessing,
            SecureShop.Domain.Enums.OrderStatus.Paid, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static string CreateSignature(string payload, string secret, long timestamp)
    {
        var signedPayload = Encoding.UTF8.GetBytes($"{timestamp}.{payload}");
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signedPayload);
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static StripeWebhookController CreateController(
        IConfiguration configuration,
        Mock<IOrderRepository>? orderRepository = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IStripeEventRepository>? stripeEvents = null)
    {
        orderRepository ??= new Mock<IOrderRepository>();
        var productRepository = new Mock<IProductRepository>();
        var paymentService = new Mock<IPaymentService>();
        var orderService = new OrderService(
            orderRepository.Object, productRepository.Object, paymentService.Object,
            new NullCacheService(), (unitOfWork ?? new Mock<IUnitOfWork>()).Object,
            (stripeEvents ?? new Mock<IStripeEventRepository>()).Object,
            NullLogger<OrderService>.Instance);
        var controller = new StripeWebhookController(
            orderService,
            configuration,
            NullLogger<StripeWebhookController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }
}
