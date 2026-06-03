using FluentValidation;
using Wasnie.Application.Common.Constants;
using Wasnie.Application.Compensation.Commands.Plans;

namespace Wasnie.Application.Compensation.Validators.Plans;

public sealed class CreatePlanCommandValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Must(c => CurrencyConstants.KnownCurrencies.Contains(c))
            .WithMessage(c => $"Currency '{c.Currency}' is not a recognized currency code. Examples: EUR, USD, GBP, PLN, CHF.");
        RuleFor(x => x.EffectiveEnd).GreaterThanOrEqualTo(x => x.EffectiveStart)
            .WithMessage("EffectiveEnd must be on or after EffectiveStart.");
    }
}
