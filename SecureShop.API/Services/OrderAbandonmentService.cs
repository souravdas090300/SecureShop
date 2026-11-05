using SecureShop.Application.Interfaces;
using SecureShop.Application.Services;

namespace SecureShop.API.Services;

/// <summary>
/// Periodically cancels PaymentIntents and orders that were created but never
/// paid, releasing their reserved stock. Runs every
/// <c>Orders:AbandonmentIntervalMinutes</c> (default 5) and abandons orders in
/// PaymentProcessing older than <c>Orders:AbandonAfterMinutes</c> (default 30).
/// Safe under multiple instances: order transitions are atomic, so stock is
/// restored exactly once regardless of how many sweeps race.
/// </summary>
public class OrderAbandonmentService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrderAbandonmentService> _logger;

    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);
    private const int DefaultAbandonAfterMinutes = 30;
    private const int BatchSize = 50;

    /// <summary>Creates the hosted service.</summary>
    public OrderAbandonmentService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OrderAbandonmentService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = _configuration.GetValue("Orders:AbandonmentIntervalMinutes", 5);
        var interval = intervalMinutes > 0 ? TimeSpan.FromMinutes(intervalMinutes) : DefaultInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order abandonment sweep failed; will retry next interval");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs one abandonment sweep. Public for testability.
    /// </summary>
    public async Task SweepOnceAsync(CancellationToken cancellationToken = default)
    {
        var abandonAfterMinutes = _configuration.GetValue("Orders:AbandonAfterMinutes", DefaultAbandonAfterMinutes);
        var cutoff = DateTime.UtcNow.AddMinutes(-abandonAfterMinutes);

        using var scope = _scopeFactory.CreateScope();
        var orderService = scope.ServiceProvider.GetRequiredService<OrderService>();
        var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();

        var orders = await orderService.GetAbandonedOrdersAsync(cutoff, BatchSize, cancellationToken);
        if (!orders.Any())
            return;

        _logger.LogInformation("Abandonment sweep found {Count} stale PaymentProcessing orders", orders.Count());

        foreach (var order in orders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var intentId = order.StripePaymentIntentId!;
            try
            {
                var state = await paymentService.GetPaymentIntentStatusAsync(intentId);
                switch (state)
                {
                    case PaymentIntentState.Succeeded:
                    case PaymentIntentState.Processing:
                        // Succeeded: payment went through — leave it for the
                        // succeeded webhook to mark Paid. Processing: Stripe
                        // cannot cancel an in-flight intent — skip until a later
                        // sweep resolves it to a terminal state.
                        _logger.LogInformation(
                            "Order {OrderId}: intent {PaymentIntentId} is {State}; skipping abandonment",
                            order.Id, intentId, state);
                        break;

                    case PaymentIntentState.Canceled:
                        // Stripe already cancelled the intent; just reconcile our side.
                        await orderService.CancelUnpaidOrderAsync(intentId, cancellationToken);
                        break;

                    case PaymentIntentState.Unknown:
                        _logger.LogWarning(
                            "Order {OrderId}: could not determine intent {PaymentIntentId} state; skipping",
                            order.Id, intentId);
                        break;

                    default:
                        // Intent is still payable — cancel it at Stripe first, then
                        // reconcile the order. If the Stripe call fails the order is
                        // left untouched and retried on the next sweep.
                        await paymentService.CancelPaymentIntentAsync(intentId);
                        await orderService.CancelUnpaidOrderAsync(intentId, cancellationToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                // Stripe outage or transient DB error — leave the order alone and
                // retry on the next sweep.
                _logger.LogWarning(ex,
                    "Abandonment failed for order {OrderId} / intent {PaymentIntentId}; will retry",
                    order.Id, intentId);
            }
        }
    }
}
