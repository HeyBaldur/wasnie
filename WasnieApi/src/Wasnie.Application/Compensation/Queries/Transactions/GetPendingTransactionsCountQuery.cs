using MediatR;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Transactions;

public sealed record GetPendingTransactionsCountQuery(
    ProcessPendingScope Scope,
    Guid? ScopeId,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd) : IRequest<Result<int>>;
