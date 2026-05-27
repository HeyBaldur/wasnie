using FluentValidation;
using Wasnie.Application.Features.Auth.Commands;

namespace Wasnie.Application.Features.Auth.Validators;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty()
            .Length(20, 500);
    }
}
