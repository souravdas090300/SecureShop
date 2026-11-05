using Microsoft.EntityFrameworkCore;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Enums;

namespace SecureShop.Infrastructure;

/// <summary>
/// EF Core implementation of <see cref="IOrderRepository"/>.
/// All queries eagerly load the <c>Items → Product</c> navigation path and
/// the owning <c>User</c> so callers receive fully populated aggregates.
/// </summary>
public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _db;

    /// <summary>Initialises the repository with the scoped EF Core context.</summary>
    public OrderRepository(AppDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Order?> GetByIdAsync(Guid id) =>
        await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Id == id);

    /// <inheritdoc />
    public async Task<Order?> GetByPaymentIntentIdAsync(
        string paymentIntentId,
        CancellationToken cancellationToken = default) =>
        await _db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.StripePaymentIntentId == paymentIntentId,
                cancellationToken);

    /// <inheritdoc />
    public async Task<OrderStatus?> GetStatusByPaymentIntentIdAsync(
        string paymentIntentId,
        CancellationToken cancellationToken = default) =>
        await _db.Orders
            .Where(o => o.StripePaymentIntentId == paymentIntentId)
            .Select(o => (OrderStatus?)o.Status)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryTransitionStatusAsync(Guid orderId, OrderStatus from, OrderStatus to, CancellationToken cancellationToken = default)
    {
        // Single conditional UPDATE — the Status = @from predicate makes the
        // transition atomic, so concurrent webhook/job deliveries cannot both win.
        var rows = await _db.Orders
            .Where(o => o.Id == orderId && o.Status == from)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, to)
                .SetProperty(o => o.UpdatedAt, DateTime.UtcNow), cancellationToken);
        return rows == 1;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Order>> GetStalePaymentProcessingOrdersAsync(DateTime createdBefore, int take, CancellationToken cancellationToken = default) =>
        await _db.Orders.Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.PaymentProcessing && o.CreatedAt < createdBefore)
            .OrderBy(o => o.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IEnumerable<Order>> GetByUserIdAsync(string userId) =>
        await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.User)
            .Where(o => o.UserId == userId)
            // Most recent orders appear first.
            .OrderByDescending(o => o.CreatedAt).ToListAsync();

    /// <inheritdoc />
    public async Task<IEnumerable<Order>> GetAllAsync() =>
        await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.User)
            .OrderByDescending(o => o.CreatedAt).ToListAsync();

    /// <inheritdoc />
    public async Task<Order> CreateAsync(Order order)
    {
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Only mark the entity modified when detached. For tracked entities EF
        // persists just the changed properties — calling Update() would mark
        // every column modified and could write a stale Status back over a
        // concurrent atomic transition.
        if (_db.Entry(order).State == EntityState.Detached)
            _db.Orders.Update(order);
        await _db.SaveChangesAsync(cancellationToken);
    }
}