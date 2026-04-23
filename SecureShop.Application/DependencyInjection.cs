using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SecureShop.Application.Services;
using SecureShop.Application.Validators;

namespace SecureShop.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services)
    {
        services.AddScoped<OrderService>();
        services.AddScoped<ProductService>();
        services.AddValidatorsFromAssemblyContaining<RegisterValidator>();

        return services;
    }
}