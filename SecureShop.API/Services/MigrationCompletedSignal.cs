namespace SecureShop.API.Services;

/// <summary>
/// Signals that the startup database migration (and role/admin seeding) has
/// finished — successfully or not. Background services that touch the
/// database await <see cref="Completion"/> so they never query a schema that
/// is still being migrated. The signal is always completed, even when startup
/// init fails, so dependents degrade to their normal retry behaviour instead
/// of hanging forever.
/// </summary>
public sealed class MigrationCompletedSignal
{
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when startup migration init has finished (success or failure).</summary>
    public Task Completion => _completion.Task;

    /// <summary>Marks startup migration init as finished. Idempotent.</summary>
    public void MarkCompleted() => _completion.TrySetResult();
}
