using Npgsql;

namespace SecureShop.Infrastructure;

/// <summary>
/// Safety guard for database-backed integration tests. Before any test
/// connects, <see cref="EnsureSafeTestConnectionString"/> requires the target
/// database name to end with "_test" and rejects hosts listed in
/// <see cref="ForbiddenHostsVariable"/>, so a misconfigured
/// SECURESHOP_TEST_DATABASE_URL cannot target a configured production host.
/// </summary>
public static class TestDatabaseGuard
{
    /// <summary>Environment variable with extra comma-separated forbidden hosts.</summary>
    public const string ForbiddenHostsVariable = "SECURESHOP_FORBIDDEN_DB_HOSTS";

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> unless the connection
    /// string targets a database whose name ends with "_test" on a host that
    /// is not listed in <c>SECURESHOP_FORBIDDEN_DB_HOSTS</c>.
    /// </summary>
    public static void EnsureSafeTestConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("SECURESHOP_TEST_DATABASE_URL is not set.");

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "SECURESHOP_TEST_DATABASE_URL is not a valid Npgsql connection string.", ex);
        }

        var database = builder.Database;
        if (string.IsNullOrEmpty(database) ||
            !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to run database-backed integration tests: database '{database}' " +
                "does not end with '_test'. Point SECURESHOP_TEST_DATABASE_URL at a " +
                "disposable test database.");
        }

        var forbiddenHosts = (Environment.GetEnvironmentVariable(ForbiddenHostsVariable) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Npgsql allows a comma-separated host list for failover — check each.
        var hosts = (builder.Host ?? string.Empty).Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var host in hosts)
        {
            if (forbiddenHosts.Contains(host))
            {
                throw new InvalidOperationException(
                    $"Refusing to run database-backed integration tests: host '{host}' is a " +
                    "known production/Railway database host. SECURESHOP_TEST_DATABASE_URL " +
                    "must point at a disposable test database.");
            }
        }
    }
}
