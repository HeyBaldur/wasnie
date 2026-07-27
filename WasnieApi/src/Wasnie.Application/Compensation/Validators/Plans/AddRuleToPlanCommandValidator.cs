using FluentValidation;
using Wasnie.Application.Compensation.Commands.Plans;

namespace Wasnie.Application.Compensation.Validators.Plans;

public sealed class AddRuleToPlanCommandValidator : AbstractValidator<AddRuleToPlanCommand>
{
    public AddRuleToPlanCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Measurement).NotNull();
        RuleFor(x => x.RateTable).NotNull();
        // A trigger the engine cannot honour must not be saveable — see TriggerValidator.
        RuleFor(x => x.Trigger!).SetValidator(new TriggerValidator()).When(x => x.Trigger is not null);
    }
}
