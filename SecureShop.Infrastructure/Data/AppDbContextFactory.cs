using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SecureShop.Infrastructure;

/// <summary>
/// Design-time factory for <see cref="AppDbContext"/>.
/// Used by EF Core tooling (<c>dotnet ef migrations add</c>, <c>dotnet ef database update</c>)
/// to create a context without a running host. Walks the directory tree upward from the
/// working directory to locate <c>SecureShop.API/appsettings.json</c> for connection string resolution.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>
    /// Creates a configured <see cref="AppDbContext"/> suitable for EF Core design-time operations.
    /// Reads the connection string from <c>appsettings.json</c>, environment-specific overrides,
    /// user secrets, and environment variables — in that priority order.
    /// </summary>
    /// <param name="args">CLI arguments forwarded from the EF Core tooling (typically empty).</param>
    /// <returns>A fully configured <see cref="AppDbContext"/> instance.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <c>DefaultConnection</c> connection string can be resolved.
    /// </exception>
    public AppDbContext CreateDbContext(string[] args)
    {
        var basePath = ResolveConfigurationBasePath();

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets<AppDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' was not found. Set it in appsettings or environment variables.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// Walks the directory tree upward from the current working directory to find the
    /// <c>SecureShop.API</c> project folder that contains <c>appsettings.json</c>.
    /// This allows the factory to work regardless of whether it is invoked from the
    /// solution root, the Infrastructure project directory, or the API project directory.
    /// </summary>
    private static string ResolveConfigurationBasePath()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            var apiProjectPath = Path.Combine(current.FullName, "SecureShop.API", "SecureShop.API.csproj");
            if (File.Exists(apiProjectPath))
            {
                return Path.GetDirectoryName(apiProjectPath)!;
            }

            var currentProjectPath = Path.Combine(current.FullName, "SecureShop.API.csproj");
            var currentSettingsPath = Path.Combine(current.FullName, "appsettings.json");
            if (File.Exists(currentProjectPath) && File.Exists(currentSettingsPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
