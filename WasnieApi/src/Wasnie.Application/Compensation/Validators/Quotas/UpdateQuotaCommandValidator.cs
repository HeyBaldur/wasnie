using FluentValidation;
using Wasnie.Application.Compensation.Commands.Quotas;

namespace Wasnie.Application.Compensation.Validators.Quotas;

public sealed class UpdateQuotaCommandValidator : AbstractValidator<UpdateQuotaCommand>
{
    public UpdateQuotaCommandValidator()
    {
        RuleFor(x => x.QuotaId).NotEmpty();
        RuleFor(x => x.MeasurementType).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.PeriodEnd).GreaterThanOrEqualTo(x => x.PeriodStart)
            .WithMessage("PeriodEnd must be on or after PeriodStart.");
        RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
    }
}
