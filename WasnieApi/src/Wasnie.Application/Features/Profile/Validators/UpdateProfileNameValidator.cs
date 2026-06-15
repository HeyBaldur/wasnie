using FluentValidation;
using Wasnie.Application.Features.Profile.Commands;

namespace Wasnie.Application.Features.Profile.Validators;

public sealed class UpdateProfileNameValidator : AbstractValidator<UpdateProfileNameCommand>
{
    public UpdateProfileNameValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty()
            .MaximumLength(100);
    }
}
