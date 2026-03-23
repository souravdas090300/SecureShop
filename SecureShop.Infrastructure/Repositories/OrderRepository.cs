using Microsoft.EntityFrameworkCore;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;

namespace SecureShop.Infrastructure;

public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _db;
    public OrderRepository(AppDbContext db) => _db = db;

    public async Task<Order?> GetByIdAsync(Guid id) =>
        await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == id);

    public async Task<IEnumerable<Order>> GetByUserIdAsync(string userId) =>
        await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Product)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt).ToListAsync();

    public async Task<Order> CreateAsync(Order order)
    {
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    public async Task UpdateAsync(Order order)
    {
        _db.Orders.Update(order);
        await _db.SaveChangesAsync();
    }
}