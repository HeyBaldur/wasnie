using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Ledger;

/// <summary>
/// The account statement for a payee. One statement per currency, because the balance is per
/// (payee, currency) and Wasnie holds no exchange rates.
/// </summary>
public sealed record GetPayeeStatementQuery(Guid PayeeId)
    : IRequest<Result<IReadOnlyList<PayeeStatementDto>>>;

public sealed record ListPayeeLedgerEntriesQuery(Guid PayeeId)
    : IRequest<Result<IReadOnlyList<PayeeLedgerEntryDto>>>;
