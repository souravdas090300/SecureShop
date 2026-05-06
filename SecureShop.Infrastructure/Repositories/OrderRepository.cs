using Microsoft.EntityFrameworkCore;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;

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
    public async Task UpdateAsync(Order order)
    {
        _db.Orders.Update(order);
        await _db.SaveChangesAsync();
    }
}