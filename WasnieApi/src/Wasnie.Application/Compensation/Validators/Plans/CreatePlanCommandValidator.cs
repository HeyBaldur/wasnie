using FluentValidation;
using Wasnie.Application.Compensation.Commands.Plans;

namespace Wasnie.Application.Compensation.Validators.Plans;

public sealed class CreatePlanCommandValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.EffectiveEnd).GreaterThanOrEqualTo(x => x.EffectiveStart)
            .WithMessage("EffectiveEnd must be on or after EffectiveStart.");
    }
}
