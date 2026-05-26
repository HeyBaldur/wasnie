using FluentValidation;
using Wasnie.Application.Compensation.Commands.Quotas;

namespace Wasnie.Application.Compensation.Validators.Quotas;

public sealed class CreateQuotaCommandValidator : AbstractValidator<CreateQuotaCommand>
{
    public CreateQuotaCommandValidator()
    {
        RuleFor(x => x.PayeeId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.PeriodEnd).GreaterThanOrEqualTo(x => x.PeriodStart)
            .WithMessage("PeriodEnd must be on or after PeriodStart.");
        RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
    }
}
