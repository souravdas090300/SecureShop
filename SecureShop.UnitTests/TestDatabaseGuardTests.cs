using FluentAssertions;
using SecureShop.Infrastructure;

namespace SecureShop.UnitTests;

public class TestDatabaseGuardTests
{
    [Fact]
    public void EnsureSafeTestConnectionString_ValidTestDatabase_DoesNotThrow()
    {
        var connectionString =
            "Host=localhost;Port=5432;Username=test;Password=test;Database=secureshop_test;SSL Mode=Disable";

        var act = () => TestDatabaseGuard.EnsureSafeTestConnectionString(connectionString);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureSafeTestConnectionString_Missing_Throws(string? connectionString)
    {
        var act = () => TestDatabaseGuard.EnsureSafeTestConnectionString(connectionString);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SECURESHOP_TEST_DATABASE_URL is not set*");
    }

    [Fact]
    public void EnsureSafeTestConnectionString_Unparseable_Throws()
    {
        var act = () => TestDatabaseGuard.EnsureSafeTestConnectionString("not a connection string");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a valid Npgsql connection string*");
    }

    [Theory]
    [InlineData("neondb")]
    [InlineData("secureshop")]
    [InlineData("secureshop_dev")]
    [InlineData("production")]
    public void EnsureSafeTestConnectionString_DatabaseNotEndingInTest_Throws(string database)
    {
        var connectionString =
            $"Host=localhost;Database={database};Username=test;Password=test";

        var act = () => TestDatabaseGuard.EnsureSafeTestConnectionString(connectionString);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not end with '_test'*");
    }

    [Fact]
    public void EnsureSafeTestConnectionString_ForbiddenHostInFailoverList_Throws()
    {
        var previous = Environment.GetEnvironmentVariable(TestDatabaseGuard.ForbiddenHostsVariable);
        try
        {
            Environment.SetEnvironmentVariable(TestDatabaseGuard.ForbiddenHostsVariable, "blocked.example.com");
            var connectionString =
                "Host=safe.example.com,blocked.example.com;Database=secureshop_test;Username=test;Password=test";

            var act = () => TestDatabaseGuard.EnsureSafeTestConnectionString(connectionString);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*production/Railway database host*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestDatabaseGuard.ForbiddenHostsVariable, previous);
        }
    }

    [Fact]
    public void EnsureSafeTestConnectionString_UsesOnlyConfiguredForbiddenHosts()
    {
        var previous = Environment.GetEnvironmentVariable(TestDatabaseGuard.ForbiddenHostsVariable);
        try
        {
            Environment.SetEnvironmentVariable(TestDatabaseGuard.ForbiddenHostsVariable, null);
            var connectionString =
                "Host=not-listed.example.com;Database=secureshop_test;Username=test;Password=test";

            var act = () => TestDatabaseGuard.EnsureSafeTestConnectionString(connectionString);

            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestDatabaseGuard.ForbiddenHostsVariable, previous);
        }
    }
}
