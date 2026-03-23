using Microsoft.Extensions.Configuration;
using SecureShop.Application.Interfaces;
using Stripe;

namespace SecureShop.Infrastructure.Services;

public class PaymentService : IPaymentService
{
    public PaymentService(IConfiguration config)
    {
        StripeConfiguration.ApiKey = config["Stripe:SecretKey"];
    }

    public async Task<string> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId)
    {
        var options = new PaymentIntentCreateOptions
        {
            Amount = (long)(amount * 100),
            Currency = currency,
            Metadata = new Dictionary<string, string>
            {
                { "orderId", orderId.ToString() }
            }
        };

        var service = new PaymentIntentService();
        var intent = await service.CreateAsync(options);
        return intent.ClientSecret;
    }
}
