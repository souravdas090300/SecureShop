using FluentValidation;
using SecureShop.Application.DTOs.Auth;

namespace SecureShop.Application.Validators;

public class RegisterValidator : AbstractValidator<RegisterDto>
{
    public RegisterValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password)
            .NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Must contain uppercase")
            .Matches("[0-9]").WithMessage("Must contain number")
            .Matches("[^a-zA-Z0-9]").WithMessage("Must contain special character");
    }
}