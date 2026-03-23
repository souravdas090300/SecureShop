using FluentValidation;
using SecureShop.Application.DTOs.Products;

namespace SecureShop.Application.Validators;

public class CreateProductValidator : AbstractValidator<CreateProductDto>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThan(0).WithMessage("Price must be > 0");
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Category).NotEmpty();
    }
}