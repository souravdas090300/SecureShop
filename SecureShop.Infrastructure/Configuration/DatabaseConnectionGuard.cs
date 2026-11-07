using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SecureShop.Infrastructure;

/// <summary>Outcome of the startup database-connection check.</summary>
public enum DatabaseConnectionStatus
{
    /// <summary>Connection string is present and safe to use.</summary>
    Ok,

    /// <summary>No connection string configured at all.</summary>
    Missing,

    /// <summary>Configured but unsafe for Production (placeholder/localhost or unparseable).</summary>
    UnsafeForProduction
}

/// <summary>
/// Startup guard that fails fast when Production would run against the
/// placeholder/localhost fallback defined in
/// <see cref="DependencyInjection.AddInfrastructureServices"/>. Diagnostics log
/// only non-secret details: presence, parsed Host/Database/Port, and the
/// NAMES of connection-related environment variables.
/// </summary>
public static class DatabaseConnectionGuard
{
    /// <summary>Configuration key the application reads (env var: ConnectionStrings__DefaultConnection).</summary>
    public const string ConnectionStringKey = "ConnectionStrings:DefaultConnection";

    /// <summary>
    /// Evaluates whether the configured connection string is safe for the
    /// current environment. Non-Production environments always pass so local
    /// development without a database keeps working.
    /// </summary>
    public static DatabaseConnectionStatus Evaluate(string? connectionString, bool isProduction)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return isProduction ? DatabaseConnectionStatus.Missing : DatabaseConnectionStatus.Ok;

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch
        {
            return isProduction ? DatabaseConnectionStatus.UnsafeForProduction : DatabaseConnectionStatus.Ok;
        }

        if (!isProduction)
            return DatabaseConnectionStatus.Ok;

        // Localhost is refused in Production — except when the database name
        // ends in "_test", which is the explicit opt-in used by integration
        // tests against a local/CI-service Postgres. A real production
        // database will not carry that suffix.
        var isTestDatabase = builder.Database?.EndsWith("_test", StringComparison.OrdinalIgnoreCase) == true;
        var unsafeHost = !isTestDatabase && builder.Host is "localhost" or "127.0.0.1" or "::1";
        var unsafeDatabase = string.Equals(builder.Database, "placeholder", StringComparison.OrdinalIgnoreCase);
        return unsafeHost || unsafeDatabase
            ? DatabaseConnectionStatus.UnsafeForProduction
            : DatabaseConnectionStatus.Ok;
    }

    /// <summary>
    /// Logs startup diagnostics for the database configuration. Never logs
    /// secrets — only presence, parsed Host/Database/Port, and environment
    /// variable names.
    /// </summary>
    public static void LogDiagnostics(IConfiguration configuration, bool isProduction, ILogger logger)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        logger.LogInformation(
            "Database config: ConnectionStrings:DefaultConnection present={Present}",
            !string.IsNullOrWhiteSpace(connectionString));

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(connectionString);
                logger.LogInformation(
                    "Database target: Host={Host}, Port={Port}, Database={Database}",
                    builder.Host, builder.Port, builder.Database);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ConnectionStrings:DefaultConnection could not be parsed by NpgsqlConnectionStringBuilder");
            }
        }

        var names = Environment.GetEnvironmentVariables().Keys
            .Cast<string>()
            .Where(k => k.StartsWith("ConnectionStrings", StringComparison.OrdinalIgnoreCase)
                     || k.StartsWith("DATABASE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        logger.LogInformation(
            "Connection-related environment variable names: {Names}",
            names.Count == 0 ? "(none)" : string.Join(", ", names));
    }
}
