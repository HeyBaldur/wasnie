using FluentValidation;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Domain.Compensation.Transactions;

namespace Wasnie.Application.Compensation.Validators.Transactions;

public sealed class IngestTransactionCommandValidator : AbstractValidator<IngestTransactionCommand>
{
    private static readonly DateOnly MinDate = new DateOnly(2000, 1, 1);

    public IngestTransactionCommandValidator()
    {
        RuleFor(x => x.ReferenceNumber).NotEmpty().MaximumLength(200);
        // PayeeId optionality is enforced by IngestTransactionHandler based on per-tenant field requirements.
        // An explicitly-provided Guid.Empty is always invalid.
        RuleFor(x => x.PayeeId)
            .Must(id => id != Guid.Empty)
            .When(x => x.PayeeId.HasValue)
            .WithMessage("PayeeId must not be an empty GUID when provided.");
        RuleFor(x => x.Amount).GreaterThan(0m);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1.");
        // Whether a selection is REQUIRED depends on the payee's candidate plans, which needs the DB —
        // that lives in IngestTransactionHandler. Here only the shape is checked.
        RuleFor(x => x.SelectedPlanAssignmentId)
            .Must(id => id != Guid.Empty)
            .When(x => x.SelectedPlanAssignmentId.HasValue)
            .WithMessage("SelectedPlanAssignmentId must not be an empty GUID when provided.");
        RuleFor(x => x.Description)
            .MaximumLength(CompensationTransaction.MaxDescriptionLength)
            .When(x => x.Description is not null);
        RuleFor(x => x.TransactionDate)
            .GreaterThanOrEqualTo(MinDate)
            .WithMessage($"TransactionDate cannot be before {MinDate:yyyy-MM-dd}.");
    }
}
