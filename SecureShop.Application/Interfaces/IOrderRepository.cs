using SecureShop.Domain.Entities;

namespace SecureShop.Application.Interfaces;

/// <summary>
/// Data-access contract for <see cref="Order"/> persistence.
/// Implementations live in the Infrastructure layer (EF Core / PostgreSQL).
/// </summary>
public interface IOrderRepository
{
    /// <summary>Returns the order with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Task<Order?> GetByIdAsync(Guid id);

    /// <summary>Returns all orders belonging to the specified user, newest first.</summary>
    Task<IEnumerable<Order>> GetByUserIdAsync(string userId);

    /// <summary>Returns every order in the system; intended for admin views only.</summary>
    Task<IEnumerable<Order>> GetAllAsync();

    /// <summary>
    /// Persists a newly created <see cref="Order"/> and returns the saved entity.
    /// </summary>
    Task<Order> CreateAsync(Order order);

    /// <summary>Persists changes to an existing <see cref="Order"/> (e.g. status updates).</summary>
    Task UpdateAsync(Order order);
}