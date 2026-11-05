using SecureShop.Domain.Entities;
using SecureShop.Domain.Enums;

namespace SecureShop.Application.Interfaces;

/// <summary>
/// Data-access contract for <see cref="Order"/> persistence.
/// Implementations live in the Infrastructure layer (EF Core / PostgreSQL).
/// </summary>
public interface IOrderRepository
{
    /// <summary>Returns the order with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Task<Order?> GetByIdAsync(Guid id);

    /// <summary>Finds an order by its Stripe PaymentIntent ID.</summary>
    Task<Order?> GetByPaymentIntentIdAsync(string paymentIntentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current status of the order for a PaymentIntent, read
    /// untracked so it reflects the latest committed value even when the
    /// caller holds a stale tracked entity.
    /// </summary>
    Task<OrderStatus?> GetStatusByPaymentIntentIdAsync(string paymentIntentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically transitions an order from <paramref name="from"/> to
    /// <paramref name="to"/> in a single conditional UPDATE. Returns
    /// <c>true</c> only when exactly one row changed — concurrent transitions
    /// lose safely instead of overwriting each other.
    /// </summary>
    Task<bool> TryTransitionStatusAsync(Guid orderId, OrderStatus from, OrderStatus to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns orders stuck in <see cref="OrderStatus.PaymentProcessing"/>
    /// created before <paramref name="createdBefore"/>, oldest first.
    /// Used by the abandonment job.
    /// </summary>
    Task<IEnumerable<Order>> GetStalePaymentProcessingOrdersAsync(DateTime createdBefore, int take, CancellationToken cancellationToken = default);

    /// <summary>Returns all orders belonging to the specified user, newest first.</summary>
    Task<IEnumerable<Order>> GetByUserIdAsync(string userId);

    /// <summary>Returns every order in the system; intended for admin views only.</summary>
    Task<IEnumerable<Order>> GetAllAsync();

    /// <summary>
    /// Persists a newly created <see cref="Order"/> and returns the saved entity.
    /// </summary>
    Task<Order> CreateAsync(Order order);

    /// <summary>Persists changes to an existing <see cref="Order"/> (e.g. status updates).</summary>
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
}