using FluentValidation;
using Wasnie.Application.Compensation.Queries.Plans;

namespace Wasnie.Application.Compensation.Validators.Plans;

/// <summary>
/// ★ THE SAME RULES THE SAVE PATH APPLIES, and deliberately not a shorter list. This endpoint takes
/// a rule definition straight from a client, so without the save-time checks it would happily
/// simulate a rule the system would never accept — and the number it returned would be a fantasy
/// about a configuration that cannot exist.
///
/// The three layers are the same three the save path has, in the same order:
///   1. here — shape (FluentValidation, mirrors <see cref="AddRuleToPlanCommandValidator"/>),
///   2. the handler — the Per Transaction cap-scope guard,
///   3. the domain — every invariant inside Plan.AddRule, reached by actually calling it.
/// </summary>
public sealed class SimulateRuleQueryValidator : AbstractValidator<SimulateRuleQuery>
{
    public SimulateRuleQueryValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.Measurement).NotNull();
        RuleFor(x => x.RateTable).NotNull();

        // A trigger the engine cannot honour must not be saveable, so it must not be simulable
        // either — otherwise the preview answers for a condition the engine would silently ignore.
        RuleFor(x => x.Trigger!).SetValidator(new TriggerValidator()).When(x => x.Trigger is not null);

        // ── The simulation's own inputs ──────────────────────────────────────
        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("The transaction amount to simulate must not be negative.");

        RuleFor(x => x.Quantity)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Quantity must be at least 1.");

        // Attainment is a ratio, not a percentage: 1.0 is quota, 1.4 is 140%. Negative attainment is
        // not a thing, and an absurd upper bound catches a caller that sent 140 meaning 1.4.
        RuleFor(x => x.AttainmentPct!.Value)
            .InclusiveBetween(0m, 100m)
            .When(x => x.AttainmentPct.HasValue)
            .WithMessage("Attainment must be a ratio between 0 and 100 (1.0 = quota reached).");

        RuleFor(x => x.QuotaTarget!.Value)
            .GreaterThan(0m)
            .When(x => x.QuotaTarget.HasValue)
            .WithMessage("Quota target must be greater than zero.");

        RuleFor(x => x.PriorCumulative!.Value)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.PriorCumulative.HasValue);
    }
}
