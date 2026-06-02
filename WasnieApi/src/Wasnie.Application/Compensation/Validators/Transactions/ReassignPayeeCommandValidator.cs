using FluentValidation;
using Wasnie.Application.Compensation.Commands.Transactions;

namespace Wasnie.Application.Compensation.Validators.Transactions;

public sealed class ReassignPayeeCommandValidator : AbstractValidator<ReassignPayeeCommand>
{
    public ReassignPayeeCommandValidator()
    {
        RuleFor(x => x.TransactionId).NotEmpty();
        RuleFor(x => x.NewPayeeId).NotEmpty();
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reassignment reason is required.")
            .MinimumLength(10).WithMessage("Reassignment reason must be at least 10 characters.");
    }
}
