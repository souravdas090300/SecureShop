using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SecureShop.Application.Services;
using SecureShop.Application.Validators;

namespace SecureShop.Application;

/// <summary>
/// Extension methods that register Application-layer services into the DI container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="OrderService"/>, <see cref="ProductService"/>,
    /// and all FluentValidation validators discovered in the Application assembly.
    /// </summary>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services)
    {
        services.AddScoped<OrderService>();
        services.AddScoped<ProductService>();
        // Auto-discover all IValidator<T> implementations in this assembly.
        services.AddValidatorsFromAssemblyContaining<RegisterValidator>();

        return services;
    }
}