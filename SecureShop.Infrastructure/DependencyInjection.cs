using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SecureShop.Application.Interfaces;
using SecureShop.Domain.Entities;
using SecureShop.Infrastructure.Services;
using StackExchange.Redis;

namespace SecureShop.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration config)
    {
        var defaultConnection = config.GetConnectionString("DefaultConnection");
        string resolvedConnStr;
        if (!string.IsNullOrWhiteSpace(defaultConnection))
        {
            try
            {
                // Parse and inject timeout settings. NpgsqlConnectionStringBuilder can throw
                // ArgumentException for unrecognised keywords — catch so startup never crashes.
                var npgsqlBuilder = new NpgsqlConnectionStringBuilder(defaultConnection)
                {
                    Timeout = 10,
                    CommandTimeout = 15
                };
                resolvedConnStr = npgsqlBuilder.ConnectionString;
            }
            catch
            {
                // Fallback: Npgsql will validate the raw string at connect time.
                resolvedConnStr = defaultConnection;
            }
        }
        else
        {
            // No connection string configured — use a placeholder so DI resolves.
            // Migration will log an error at startup; set ConnectionStrings__DefaultConnection in Railway.
            resolvedConnStr = "Host=localhost;Database=placeholder;Username=placeholder;Password=placeholder";
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(resolvedConnStr,
                npgsql => npgsql.EnableRetryOnFailure(3).CommandTimeout(15)));

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.User.RequireUniqueEmail = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        var redisConnectionString = config.GetConnectionString("Redis");
        // Deferred singleton: do NOT call Connect() here — synchronous Connect()
        // blocks DI registration for ConnectTimeout (5 s) on invalid hostnames.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            if (string.IsNullOrWhiteSpace(redisConnectionString))
                return null!;
            try
            {
                var opts = ConfigurationOptions.Parse(redisConnectionString);
                opts.ConnectTimeout = 3000;
                opts.AbortOnConnectFail = false;
                return ConnectionMultiplexer.Connect(opts);
            }
            catch
            {
                return null!;
            }
        });
        services.AddScoped<ICacheService>(sp =>
        {
            var conn = sp.GetService<IConnectionMultiplexer>();
            return conn != null ? new CacheService(conn) : new NullCacheService();
        });
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();

        return services;
    }
}