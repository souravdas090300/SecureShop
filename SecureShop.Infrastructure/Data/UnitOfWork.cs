using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SecureShop.Application.Interfaces;

namespace SecureShop.Infrastructure;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/> backed by the scoped
/// <see cref="AppDbContext"/>. Reuses an ambient transaction when nested so
/// callers can compose repository calls inside one atomic unit.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    /// <summary>Initialises the unit of work with the scoped DbContext.</summary>
    public UnitOfWork(AppDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        // EnableRetryOnFailure forbids user-initiated transactions outside the
        // execution strategy, so the whole unit (including any retries) runs
        // inside it. A retried unit re-executes `work` from scratch, which is
        // safe because the Stripe event insert is the idempotency gate.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // Already inside a transaction (e.g. a webhook event wrapper) — join it
            // instead of opening a nested one, which EF Core does not support.
            if (_db.Database.CurrentTransaction is not null)
                return await work();

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await work();
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
}
