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
    }
}
