using SecureShop.Domain.Entities;

namespace SecureShop.Application.Interfaces;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id);
    Task<IEnumerable<Order>> GetByUserIdAsync(string userId);
    Task<Order> CreateAsync(Order order);
    Task UpdateAsync(Order order);
}