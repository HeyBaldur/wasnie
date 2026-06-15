using FluentValidation;
using Wasnie.Application.Features.Profile.Commands;

namespace Wasnie.Application.Features.Profile.Validators;

public sealed class RequestEmailChangeValidator : AbstractValidator<RequestEmailChangeCommand>
{
    public RequestEmailChangeValidator()
    {
        RuleFor(x => x.NewEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);
    }
}
