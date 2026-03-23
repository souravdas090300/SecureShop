namespace SecureShop.Application.Interfaces;

public interface IPaymentService
{
    Task<string> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId);
}