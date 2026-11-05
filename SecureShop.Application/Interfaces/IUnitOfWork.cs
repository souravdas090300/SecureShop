namespace SecureShop.Application.Interfaces;

/// <summary>
/// Runs work inside a single database transaction. Repositories that share the
/// same scoped DbContext join the ambient transaction automatically, and nested
/// calls reuse the outermost transaction.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Executes <paramref name="work"/> atomically: commits on success, rolls
    /// back and rethrows on failure.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default);
}
