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

/// <summary>
/// Earnings AND debt for one payee, aggregated — the answer to "what is X's balance".
///
/// ★ IT AGGREGATES ON THE SERVER, ON PURPOSE. The alternative — paging the payouts list and summing the
/// pages — would put a money total at the mercy of a page size, and the first payee with 26 payouts
/// would be under-reported by whoever forgot to follow the pages.
/// </summary>
/// <param name="Period">
/// A PeriodHelper token ("this-month", "last-month", "this-quarter", "ytd", "all-time"…). Unknown values
/// degrade to no date filter, which is PeriodHelper's own contract — a bad token must not throw on a
/// read this cheap.
/// </param>
public sealed record GetPayeeLedgerSummaryQuery(Guid PayeeId, string Period = "all-time")
    : IRequest<Result<PayeeLedgerSummaryDto>>;

/// <summary>
/// Payees who have LEFT and whose ledger balance is not zero — the accounts the engine has stopped
/// processing and that only a person can close.
///
/// This is the other half of the circuit breaker. Excluding a terminated payee from pay runs stops the
/// ghost, but on its own it would also make their debt invisible, which is how debt quietly disappears.
/// Finance needs a list of exactly these people to act on.
/// </summary>
public sealed record ListTerminatedPayeesWithBalanceQuery
    : IRequest<Result<IReadOnlyList<TerminatedPayeeBalanceDto>>>;
