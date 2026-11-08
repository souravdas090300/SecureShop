using FluentAssertions;
using SecureShop.Infrastructure;

namespace SecureShop.UnitTests;

public class DatabaseConnectionGuardTests
{
    private const string NeonStyle =
        "Host=ep-example.us-east-2.aws.neon.tech;Username=neondb_owner;Password=secret;Database=neondb;SSL Mode=Require";

    private const string Placeholder =
        "Host=localhost;Database=placeholder;Username=placeholder;Password=placeholder";

    [Fact]
    public void Evaluate_Production_WithRealDatabase_ReturnsOk()
    {
        DatabaseConnectionGuard.Evaluate(NeonStyle, isProduction: true)
            .Should().Be(DatabaseConnectionStatus.Ok);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Evaluate_Production_WithoutConnectionString_ReturnsMissing(string? connStr)
    {
        DatabaseConnectionGuard.Evaluate(connStr, isProduction: true)
            .Should().Be(DatabaseConnectionStatus.Missing);
    }

    [Fact]
    public void Evaluate_Production_WithPlaceholderFallback_ReturnsUnsafe()
    {
        // This is the exact string DependencyInjection falls back to when the
        // env var is absent — Production must refuse to start with it.
        DatabaseConnectionGuard.Evaluate(Placeholder, isProduction: true)
            .Should().Be(DatabaseConnectionStatus.UnsafeForProduction);
    }

    [Theory]
    [InlineData("Host=localhost;Database=realdb;Username=u;Password=p")]
    [InlineData("Host=127.0.0.1;Database=realdb;Username=u;Password=p")]
    [InlineData("Host=::1;Database=realdb;Username=u;Password=p")]
    public void Evaluate_Production_WithLocalhostHost_ReturnsUnsafe(string connStr)
    {
        DatabaseConnectionGuard.Evaluate(connStr, isProduction: true)
            .Should().Be(DatabaseConnectionStatus.UnsafeForProduction);
    }

    [Theory]
    [InlineData("Host=localhost;Database=secureshop_test;Username=u;Password=p")]
    [InlineData("Host=127.0.0.1;Port=5432;Database=shop_test;Username=u;Password=p;SSL Mode=Disable")]
    public void Evaluate_Production_WithLocalhostTestDatabase_ReturnsOk(string connStr)
    {
        // Explicit opt-in: a *_test database on localhost is allowed so
        // integration tests can run against a local or CI-service Postgres.
        DatabaseConnectionGuard.Evaluate(connStr, isProduction: true)
            .Should().Be(DatabaseConnectionStatus.Ok);
    }

    [Fact]
    public void Evaluate_Production_WithDatabaseNamedPlaceholder_ReturnsUnsafe()
    {
        DatabaseConnectionGuard.Evaluate(
                "Host=db.example.com;Database=placeholder;Username=u;Password=p",
                isProduction: true)
            .Should().Be(DatabaseConnectionStatus.UnsafeForProduction);
    }

    [Fact]
    public void Evaluate_Production_WithUnparseableString_ReturnsUnsafe()
    {
        DatabaseConnectionGuard.Evaluate("not a connection string {{{", isProduction: true)
            .Should().Be(DatabaseConnectionStatus.UnsafeForProduction);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Placeholder)]
    [InlineData("Host=localhost;Database=devdb;Username=u;Password=p")]
    public void Evaluate_NonProduction_AlwaysReturnsOk(string? connStr)
    {
        // Local development without a database must keep working.
        DatabaseConnectionGuard.Evaluate(connStr, isProduction: false)
            .Should().Be(DatabaseConnectionStatus.Ok);
    }
}
