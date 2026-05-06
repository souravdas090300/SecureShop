using FluentValidation;
using SecureShop.Application.DTOs.Auth;

namespace SecureShop.Application.Validators;

/// <summary>
/// FluentValidation validator for <see cref="RegisterDto"/>.
/// Enforces name length limits, a valid e-mail address, and a strong password policy
/// (min 8 chars, upper, lower, digit, and special character).
/// </summary>
public class RegisterValidator : AbstractValidator<RegisterDto>
{
    /// <summary>Configures all validation rules for user registration.</summary>
    public RegisterValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password)
            .NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter")
            .Matches("[0-9]").WithMessage("Password must contain at least one number")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character");
    }
}