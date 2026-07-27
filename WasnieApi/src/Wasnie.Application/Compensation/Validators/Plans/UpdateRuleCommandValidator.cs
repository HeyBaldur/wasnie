using FluentValidation;
using Wasnie.Application.Compensation.Commands.Plans;

namespace Wasnie.Application.Compensation.Validators.Plans;

/// <summary>
/// There was no validator for rule EDITS at all — only for adds. So even once adds are guarded, an
/// unhonourable trigger could still be introduced by editing an existing rule.
/// </summary>
public sealed class UpdateRuleCommandValidator : AbstractValidator<UpdateRuleCommand>
{
    public UpdateRuleCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.RuleId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Measurement).NotNull();
        RuleFor(x => x.RateTable).NotNull();
        RuleFor(x => x.Trigger!).SetValidator(new TriggerValidator()).When(x => x.Trigger is not null);
    }
}
