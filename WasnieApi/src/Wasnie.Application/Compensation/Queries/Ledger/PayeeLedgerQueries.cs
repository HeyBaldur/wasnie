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
/// Payees who have LEFT with something still open — the accounts the engine has stopped processing and
/// that only a person can close.
///
/// This is the other half of the circuit breaker. Excluding a terminated payee from pay runs stops the
/// ghost, but on its own it would also make what they are owed invisible, which is how money quietly
/// disappears. Finance needs a list of exactly these people to act on.
///
/// ★ "OPEN" USED TO MEAN ONLY "ledger balance != 0", AND THAT DEFINITION HAD A HOLE THE SIZE OF EVERY
/// UNPAID COMMISSION. The ledger records what a payee OWES; commission they EARNED and were never paid
/// leaves no ledger row at all. So a departed payee holding an active, unconsumed credit had no balance
/// row, did not appear here, and was skipped by the pay run for being terminated — invisible on both
/// sides at once (see docs/DIAG_POL-8554_PAYOUT_Y_CREDITOS_INVENTADOS.md, where €3,869.34 sat in that
/// gap). The queue now starts from the terminated payees themselves and reports BOTH kinds of open item.
///
/// ★ IT STILL ONLY REPORTS. Nothing here pays, settles or unblocks anything, and the pay-run guard that
/// skips terminated payees is deliberately untouched: a final settlement is negotiated, and Mark as paid
/// is irreversible.
/// </summary>
public sealed record ListTerminatedPayeesWithBalanceQuery
    : IRequest<Result<TerminatedAccountsDto>>;
