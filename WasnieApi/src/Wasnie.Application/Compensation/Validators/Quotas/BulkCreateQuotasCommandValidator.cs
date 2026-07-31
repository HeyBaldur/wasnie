using FluentValidation;
using Wasnie.Application.Compensation.Commands.Quotas;

namespace Wasnie.Application.Compensation.Validators.Quotas;

/// <summary>
/// Request SHAPE only, mirroring <see cref="CreateQuotaCommandValidator"/> field for field. The one
/// payee id becomes a list; every other rule is the same rule.
///
/// Deliberately contains no business validation: whether a quota may exist is decided by the domain
/// through QuotaBuilder, identically for one quota and for a hundred.
/// </summary>
public sealed class BulkCreateQuotasCommandValidator : AbstractValidator<BulkCreateQuotasCommand>
{
    /// <summary>
    /// An upper bound on ONE request, not a business rule about quotas: the batch is a single
    /// transaction, and an unbounded list would hold it open for as long as someone cares to make it.
    /// Far above any real "assign this quarter's target to the team".
    /// </summary>
    public const int MaxPayeesPerBatch = 200;

    public BulkCreateQuotasCommandValidator()
    {
        RuleFor(x => x.PayeeIds).NotEmpty()
            .WithMessage("Select at least one payee.");
        RuleFor(x => x.PayeeIds).Must(ids => ids.Count <= MaxPayeesPerBatch)
            .WithMessage($"A single batch can cover at most {MaxPayeesPerBatch} payees.");
        RuleForEach(x => x.PayeeIds).NotEmpty();

        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.PeriodEnd).GreaterThanOrEqualTo(x => x.PeriodStart)
            .WithMessage("PeriodEnd must be on or after PeriodStart.");
        RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
    }
}
