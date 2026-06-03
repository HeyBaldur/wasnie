using FluentValidation;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Commands.Transactions;

namespace Wasnie.Application.Compensation.Validators.Transactions;

public sealed class ProcessPendingTransactionsCommandValidator : AbstractValidator<ProcessPendingTransactionsCommand>
{
    public ProcessPendingTransactionsCommandValidator()
    {
        RuleFor(x => x.ScopeId)
            .NotEmpty()
            .When(x => x.Scope != ProcessPendingScope.ByPayeeAndPeriod)
            .WithMessage("ScopeId is required for ByPlanAssignment and ByPlan scopes.");

        RuleFor(x => x.PeriodStart)
            .NotNull()
            .When(x => x.Scope == ProcessPendingScope.ByPayeeAndPeriod)
            .WithMessage("PeriodStart is required for ByPayeeAndPeriod scope.");

        RuleFor(x => x.PeriodEnd)
            .NotNull()
            .When(x => x.Scope == ProcessPendingScope.ByPayeeAndPeriod)
            .WithMessage("PeriodEnd is required for ByPayeeAndPeriod scope.");

        RuleFor(x => x)
            .Must(x => x.PeriodStart is null || x.PeriodEnd is null || x.PeriodEnd >= x.PeriodStart)
            .WithMessage("PeriodEnd must be on or after PeriodStart.");

        RuleFor(x => x.ScopeId)
            .NotEmpty()
            .When(x => x.Scope == ProcessPendingScope.ByPayeeAndPeriod)
            .WithMessage("ScopeId (PayeeId) is required for ByPayeeAndPeriod scope.");
    }
}
