using FluentAssertions;
using SecureShop.Infrastructure;

namespace SecureShop.UnitTests;

public class TestDatabaseGuardTests
{
    private const string SafeTestConnection =
        "Host=ep-example-pooler.c-7.us-east-2.aws.neon.tech;Database=secureshop_test;" +
        "Username=neondb_owner;Password=secret;SSL Mode=Require";

    private const string ProductionNeonHost =
        "ep-snowy-paper-agsz6ldf.c-2.eu-central-1.aws.neon.tech";

    [Fact]
    public void EnsureSafeTestConnectionString_ValidTestDatabase_DoesNotThrow()
    {
        var act = () => TestDatabaseGuard.EnsureSafeTestConnectionString(SafeTestConnection);
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureSafeTestConnectionString_LocalhostTestDatabase_DoesNotThrow()
    {
        // CI runs against a local PostgreSQL service container.
        var connStr = "Host=localhost;Port=5432;Username=postgres;Password=postgres;" +
                      "Database=secureshop_test;SSL Mode=Disable";
        var act = () => TestDatabaseGuard.EnsureSafeTestConnectionString(connStr);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureSafeTestConnectionString_Missing_Throws(string? connStr)
    {
        var act = () => TestDatabaseGuard.EnsureSafeTestConnectionString(connStr);
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
    [InlineData("ostra")]
    public void EnsureSafeTestConnectionString_DatabaseNotEndingInTest_Throws(string database)
    {
        var connStr = $"Host=ep-example.c-7.us-east-2.aws.neon.tech;Database={database};" +
                      "Username=u;Password=p;SSL Mode=Require";
        var act = () => TestDatabaseGuard.EnsureSafeTestConnectionString(connStr);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not end with '_test'*");
    }

    [Fact]
    public void EnsureSafeTestConnectionString_ProductionNeonHost_Throws()
    {
        var connStr = $"Host={ProductionNeonHost};Database=secureshop_test;" +
                      "Username=u;Password=p;SSL Mode=Require";
        var act = () => TestDatabaseGuard.EnsureSafeTestConnectionString(connStr);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*production/Railway database host*");
    }

    [Fact]
    public void EnsureSafeTestConnectionString_ProductionHostInFailoverList_Throws()
    {
        // Npgsql supports comma-separated hosts for failover — every entry is checked.
        var connStr = $"Host=safe.example.com,{ProductionNeonHost};Database=secureshop_test;" +
                      "Username=u;Password=p;SSL Mode=Require";
        var act = () => TestDatabaseGuard.EnsureSafeTestConnectionString(connStr);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*production/Railway database host*");
    }

    [Fact]
    public void EnsureSafeTestConnectionString_ExtraForbiddenHostFromEnv_Throws()
    {
        var previous = Environment.GetEnvironmentVariable(TestDatabaseGuard.ForbiddenHostsVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                TestDatabaseGuard.ForbiddenHostsVariable, "db.example.com, other.example.com");
            var connStr = "Host=other.example.com;Database=secureshop_test;" +
                          "Username=u;Password=p;SSL Mode=Require";
            var act = () => TestDatabaseGuard.EnsureSafeTestConnectionString(connStr);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*production/Railway database host*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestDatabaseGuard.ForbiddenHostsVariable, previous);
        }
    }
}
