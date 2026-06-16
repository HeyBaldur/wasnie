using FluentValidation;
using Wasnie.Application.Features.Auth.Commands;

namespace Wasnie.Application.Features.Auth.Validators;

public sealed class RegenerateRecoveryCodesCommandValidator : AbstractValidator<RegenerateRecoveryCodesCommand>
{
    public RegenerateRecoveryCodesCommandValidator()
    {
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Current password is required.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Verification code is required.")
            .Length(6).WithMessage("Verification code must be 6 digits.")
            .Matches("^[0-9]+$").WithMessage("Verification code must contain only digits.");
    }
}
