using FluentValidation;
using Wasnie.Application.Compensation.Commands.Transactions;

namespace Wasnie.Application.Compensation.Validators.Transactions;

public sealed class IngestTransactionCommandValidator : AbstractValidator<IngestTransactionCommand>
{
    private static readonly DateOnly MinDate = new DateOnly(2000, 1, 1);

    public IngestTransactionCommandValidator()
    {
        RuleFor(x => x.ReferenceNumber).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PayeeId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0m);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.TransactionDate)
            .GreaterThanOrEqualTo(MinDate)
            .WithMessage($"TransactionDate cannot be before {MinDate:yyyy-MM-dd}.");
    }
}
