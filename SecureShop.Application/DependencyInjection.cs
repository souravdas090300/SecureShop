using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SecureShop.Application.DTOs.Auth;
using SecureShop.Application.DTOs.Products;
using SecureShop.Application.Services;
using SecureShop.Application.Validators;

namespace SecureShop.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ProductService>();
        services.AddScoped<OrderService>();
        services.AddScoped<IValidator<CreateProductDto>, CreateProductValidator>();
        services.AddScoped<IValidator<RegisterDto>, RegisterValidator>();
        return services;
    }
}