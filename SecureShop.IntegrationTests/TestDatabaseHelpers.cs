using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureShop.API.Services;
using SecureShop.Infrastructure;
using Xunit;

namespace SecureShop.IntegrationTests;

public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SECURESHOP_TEST_DATABASE_URL"))
            && !string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Skipped: SECURESHOP_TEST_DATABASE_URL is not set. Configure a disposable *_test PostgreSQL database to run this test.";
        }
    }
}

internal static class TestDatabaseHelpers
{
    public static string RequireConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SECURESHOP_TEST_DATABASE_URL is not set. DB-backed integration tests must run in CI and may be skipped locally only.");
        }

        return connectionString;
    }

    public static async Task WaitForMigrationsAsync(IServiceProvider services)
    {
        var signal = services.GetRequiredService<MigrationCompletedSignal>();
        try
        {
            await signal.Completion.WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("Startup database migration did not finish within 120s.");
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.Database.GetPendingMigrationsAsync();
        if (pending.Any())
        {
            throw new InvalidOperationException(
                "Startup migration did not apply all migrations. Pending: " +
                string.Join(", ", pending));
        }
    }
}
