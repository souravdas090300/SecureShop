using Microsoft.EntityFrameworkCore;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;

namespace SecureShop.Infrastructure;

/// <summary>
/// EF Core implementation of <see cref="IStripeEventRepository"/>.
/// Records processed Stripe events in the ProcessedStripeEvents ledger.
/// </summary>
public class StripeEventRepository : IStripeEventRepository
{
    private readonly AppDbContext _db;

    /// <summary>Initialises the repository with the scoped DbContext.</summary>
    public StripeEventRepository(AppDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<bool> TryRecordProcessedEventAsync(string eventId, string eventType, CancellationToken ct = default)
    {
        // INSERT ... ON CONFLICT DO NOTHING: concurrent duplicate deliveries
        // block on the speculative insertion until the winner commits, then
        // see the conflict and affect 0 rows — exactly-once across parallel posts.
        var inserted = await _db.Database.ExecuteSqlAsync($"""
            INSERT INTO "ProcessedStripeEvents" ("EventId", "Type", "ProcessedAt")
            VALUES ({eventId}, {eventType}, {DateTime.UtcNow})
            ON CONFLICT ("EventId") DO NOTHING
            """, ct);
        return inserted == 1;
    }
}
