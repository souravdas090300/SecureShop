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
        var defaultConnection = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection not configured");

        // Prevent startup from hanging indefinitely on remote database issues.
        var npgsqlBuilder = new NpgsqlConnectionStringBuilder(defaultConnection)
        {
            Timeout = 10,
            CommandTimeout = 15
        };

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                npgsqlBuilder.ConnectionString,
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
        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            try
            {
                var redisConfig = ConfigurationOptions.Parse(redisConnectionString);
                redisConfig.ConnectTimeout = 5000;
                redisConfig.AbortOnConnectFail = false;
                var redis = ConnectionMultiplexer.Connect(redisConfig);
                services.AddSingleton<IConnectionMultiplexer>(redis);
                services.AddScoped<ICacheService, CacheService>();
            }
            catch
            {
                services.AddScoped<ICacheService, NullCacheService>();
            }
        }
        else
        {
            services.AddScoped<ICacheService, NullCacheService>();
        }
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();

        return services;
    }
}