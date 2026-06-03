using MediatR;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Transactions;

public sealed record ProcessPendingTransactionsCommand(
    ProcessPendingScope Scope,
    Guid? ScopeId,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd) : IRequest<Result<ProcessPendingTransactionsResult>>, IMoneyCriticalCommand
{
    public string AuditAction => AuditActions.PendingTransactionsProcessed;
    public string AuditResourceType => ResourceTypes.Transaction;
    public string? AuditResourceId { get; set; }
    public string? AuditDisplayName => $"ProcessPending:{Scope}";
}

public sealed record ProcessPendingTransactionsResult(Guid JobId, int CandidateCount);
