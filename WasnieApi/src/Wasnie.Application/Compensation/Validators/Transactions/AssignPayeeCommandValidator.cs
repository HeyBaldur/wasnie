using FluentValidation;
using Wasnie.Application.Compensation.Commands.Transactions;

namespace Wasnie.Application.Compensation.Validators.Transactions;

public sealed class AssignPayeeCommandValidator : AbstractValidator<AssignPayeeCommand>
{
    public AssignPayeeCommandValidator()
    {
        RuleFor(x => x.TransactionId).NotEmpty();
        RuleFor(x => x.PayeeId).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(1000).When(x => x.Comment != null);
    }
}
