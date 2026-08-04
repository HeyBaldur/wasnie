using FluentValidation;
using Wasnie.Application.Common.Constants;
using Wasnie.Application.Compensation.Commands.Plans;

namespace Wasnie.Application.Compensation.Validators.Plans;

/// <summary>
/// Request SHAPE only: the plan half mirrors <see cref="CreatePlanCommandValidator"/> field for field,
/// and the quota half mirrors <c>CreateQuotaCommandValidator</c> field for field (minus PlanId, which
/// this request does not carry — the plan is being created by it).
///
/// Deliberately contains no business validation. Whether a plan and a quota may exist is decided by
/// the domain, through Plan.Create and QuotaBuilder, identically to the two single-entity forms.
/// </summary>
public sealed class CreatePlanWithQuotaCommandValidator : AbstractValidator<CreatePlanWithQuotaCommand>
{
    /// <summary>
    /// An upper bound on ONE request, not a business rule about quotas: the request is a single
    /// transaction, and an unbounded list would hold it open for as long as someone cares to make it.
    /// Same bound and same reason as the bulk quota endpoint.
    /// </summary>
    public const int MaxQuotasPerRequest = 200;

    public CreatePlanWithQuotaCommandValidator()
    {
        // ── Plan: identical to CreatePlanCommandValidator ─────────────────────
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Must(c => CurrencyConstants.KnownCurrencies.Contains(c))
            .WithMessage(c => $"Currency '{c.Currency}' is not a recognized currency code. Examples: EUR, USD, GBP, PLN, CHF.");
        RuleFor(x => x.EffectiveEnd).GreaterThanOrEqualTo(x => x.EffectiveStart)
            .WithMessage("EffectiveEnd must be on or after EffectiveStart.");

        // At least one quota, because a plan with none is what POST /api/plans already creates.
        // Accepting an empty list here would make this a second way to do that — and the moment there
        // are two ways, they start to differ.
        RuleFor(x => x.Quotas).NotEmpty()
            .WithMessage("Provide at least one quota. To create a plan without quotas, use POST /api/plans.");
        RuleFor(x => x.Quotas).Must(q => q.Count <= MaxQuotasPerRequest)
            .WithMessage($"A single request can carry at most {MaxQuotasPerRequest} quotas.");

        // ── Quota: identical to CreateQuotaCommandValidator ───────────────────
        RuleForEach(x => x.Quotas).ChildRules(q =>
        {
            q.RuleFor(x => x.PayeeId).NotEmpty();
            q.RuleFor(x => x.Amount).GreaterThan(0);
            q.RuleFor(x => x.Currency).NotEmpty().Length(3);
            q.RuleFor(x => x.PeriodEnd).GreaterThanOrEqualTo(x => x.PeriodStart)
                .WithMessage("PeriodEnd must be on or after PeriodStart.");
            q.RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
        });
    }
}
