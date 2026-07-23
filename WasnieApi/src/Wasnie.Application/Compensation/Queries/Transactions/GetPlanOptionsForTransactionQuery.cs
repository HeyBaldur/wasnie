using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Transactions;

/// <summary>
/// The plans a transaction for this payee/date/currency could be credited to. Drives the mandatory
/// plan selector on the manual transaction form; the same candidate rule the engine uses.
/// </summary>
public sealed record GetPlanOptionsForTransactionQuery(
    Guid PayeeId,
    DateOnly TransactionDate,
    string Currency) : IRequest<Result<PlanOptionsDto>>;
