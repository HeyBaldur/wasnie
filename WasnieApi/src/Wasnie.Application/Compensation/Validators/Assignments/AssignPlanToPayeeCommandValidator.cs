using FluentValidation;
using Wasnie.Application.Compensation.Commands.Assignments;

namespace Wasnie.Application.Compensation.Validators.Assignments;

public sealed class AssignPlanToPayeeCommandValidator : AbstractValidator<AssignPlanToPayeeCommand>
{
    public AssignPlanToPayeeCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.PayeeId).NotEmpty();
        RuleFor(x => x.EffectiveEnd).GreaterThanOrEqualTo(x => x.EffectiveStart)
            .WithMessage("EffectiveEnd must be on or after EffectiveStart.");
    }
}
