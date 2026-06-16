using FluentValidation;
using Wasnie.Application.Features.Auth.Commands;

namespace Wasnie.Application.Features.Auth.Validators;

public sealed class VerifyTwoFactorLoginCommandValidator : AbstractValidator<VerifyTwoFactorLoginCommand>
{
    public VerifyTwoFactorLoginCommandValidator()
    {
        RuleFor(x => x.ChallengeToken)
            .NotEmpty().WithMessage("Challenge token is required.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Verification code is required.")
            .MaximumLength(20).WithMessage("Code must not exceed 20 characters.");
    }
}
