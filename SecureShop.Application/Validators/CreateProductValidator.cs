using FluentValidation;
using SecureShop.Application.DTOs.Products;

namespace SecureShop.Application.Validators;

/// <summary>
/// FluentValidation validator for <see cref="CreateProductDto"/>.
/// Enforces that a product has a non-empty name (max 200 chars),
/// a positive price, non-negative stock, and a non-empty category.
/// </summary>
public class CreateProductValidator : AbstractValidator<CreateProductDto>
{
    /// <summary>Configures validation rules for product creation.</summary>
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThan(0).WithMessage("Price must be > 0");
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Category).NotEmpty();
    }
}