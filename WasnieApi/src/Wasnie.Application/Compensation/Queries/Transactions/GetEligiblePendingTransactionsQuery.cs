using MediatR;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Transactions;

public sealed record GetEligiblePendingTransactionsQuery(
    ProcessPendingScope Scope,
    Guid? ScopeId,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd) : IRequest<Result<EligiblePendingResult>>;

public sealed record EligiblePendingResult(
    IReadOnlyList<EligiblePendingTransactionDto> Transactions,
    int TotalCount);

public sealed record EligiblePendingTransactionDto(
    Guid Id,
    Guid? PayeeId,
    string ReferenceNumber,
    string? PayeeName,
    string? PayeeCode,
    DateOnly TransactionDate,
    decimal Amount,
    string Currency);
