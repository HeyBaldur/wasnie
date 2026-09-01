using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Ledger;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Payees;

namespace Wasnie.Application.Compensation.Handlers.Ledger;

/// <summary>
/// The accounts nobody is processing any more: payees who have LEFT with something still open.
///
/// Terminating someone takes them out of every future pay run — which is right, because they will earn
/// nothing more and a ghost in the engine is worse than useless. But an obligation that nobody can see
/// is how money quietly evaporates, so the freeze and this list ship together: the engine stops, finance
/// gets a work queue, and the account is closed by a person.
///
/// ★★ THIS QUERY NO LONGER STARTS FROM PayeeBalances, AND THAT IS THE WHOLE FIX. It used to, and the
/// consequence was a blind spot with money in it: the ledger records what a payee OWES, so an active
/// commission credit that no payout ever consumed produces NO balance row. A departed payee owed
/// €3,869.34 therefore had no row here, while the pay run skipped her for being terminated — invisible
/// on both sides at once (docs/DIAG_POL-8554_PAYOUT_Y_CREDITOS_INVENTADOS.md). Starting from the
/// TERMINATED PAYEES and asking each source what it holds is the only shape where "no balance row"
/// cannot mean "nothing outstanding".
///
/// ★ AND IT ONLY REPORTS. No write, no settlement, no change to the pay-run guard. Wasnie makes the open
/// account impossible to overlook and records the decision that finance makes; it neither collects the
/// money nor decides which kind of closure it is.
/// </summary>
public sealed class ListTerminatedPayeesWithBalanceHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPayeeAccessGuard payeeAccessGuard)
    : IRequestHandler<ListTerminatedPayeesWithBalanceQuery, Result<TerminatedAccountsDto>>
{
    public async Task<Result<TerminatedAccountsDto>> Handle(
        ListTerminatedPayeesWithBalanceQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.LedgerRead, cancellationToken);

        // ★ A LIST CANNOT BE PROTECTED BY A PER-RESOURCE CHECK — IT HAS TO BE FILTERED. This endpoint
        // takes no payee id: it IS the list of which payees to look at, which made it the widest leak
        // of the three. Every departed colleague's outstanding money, in one call, to anyone holding
        // Ledger.Read (Rep included).
        //
        // FILTERED, NOT REFUSED. Trimming the rows to what the caller may see is both the safe answer
        // and the useful one: a manager legitimately sees a departed report here, and a wholesale 403
        // would take that away to no benefit. For a Rep the visible set is their own payee, so the
        // list is empty or their own closed account — nothing about anyone else, and no way to tell
        // an empty result from a tenant with no orphan accounts at all.
        var visibility = await payeeAccessGuard.GetVisibilityAsync(cancellationToken);

        // Null = no filter (supervisory role). An ARRAY rather than the set itself so EF translates the
        // membership test into a plain IN (...) instead of tripping over IReadOnlySet at query time.
        var visibleIds = visibility.IsUnrestricted ? null : visibility.PayeeIds.ToArray();

        // ── Step 1: WHO. The population is the departed, not the indebted ─────────────────────────────
        // Every table below is asked "what do you hold for these people", so a source with nothing to
        // say cannot silently remove somebody from the queue. The tenant query filter scopes this.
        var terminated = await db.Payees
            .Where(p => p.Status == PayeeStatus.Terminated
                     && (visibleIds == null || visibleIds.Contains(p.Id)))
            .Select(p => new { p.Id, p.FullName, p.EmployeeCode, p.TerminationDate, p.AccountClosedAt })
            .ToListAsync(cancellationToken);

        if (terminated.Count == 0)
        {
            return Result<TerminatedAccountsDto>.Success(new TerminatedAccountsDto([], []));
        }

        var payeeIds = terminated.Select(p => p.Id).ToList();

        // ── Step 2: the ledger side, unchanged ────────────────────────────────────────────────────────
        // Still "balance != 0": a settled ledger account is not work. What changed is that this is no
        // longer the ONLY thing that can put a payee on the list.
        var balances = await db.PayeeBalances
            .Where(b => payeeIds.Contains(b.PayeeId) && b.Balance.Amount != 0m)
            .Select(b => new { b.PayeeId, b.Currency, Amount = b.Balance.Amount, b.UpdatedAt })
            .ToListAsync(cancellationToken);

        // ── Step 3: the side that was missing ─────────────────────────────────────────────────────────
        // An UNSETTLED credit is active, unconsumed and unclosed: SupersededAt == null (it was not
        // replaced by a reallocation), ConsumedAt == null (no Paid payout ever took it) and
        // ClosedAt == null (finance has not settled or written it off). Those three nulls are the
        // engine's own definition of a credit still awaiting money — the same trio CalculatePayouts
        // filters on — so this list cannot drift from what a pay run would have picked up.
        //
        // The entities are materialised rather than projected because RuleSnapshot is a JSON-converted
        // column: `c.RuleSnapshot.RuleName` has no SQL translation. Same shape ListCreditsHandler uses.
        var credits = await db.Credits
            .Where(c => payeeIds.Contains(c.PayeeId)
                     && c.SupersededAt == null
                     && c.ConsumedAt == null
                     // ★ AND NOT CLOSED. This is what makes closing an account remove it from the
                     // queue: membership is DERIVED from money, so a payee leaves because there is
                     // nothing left in it — not because a flag hides them. Payee.AccountClosedAt is
                     // recorded but deliberately never filtered here, so a credit that arrives after
                     // the closure brings the row straight back instead of vanishing.
                     && c.ClosedAt == null)
            .ToListAsync(cancellationToken);

        var referenceById = new Dictionary<Guid, string>();
        var planNameById = new Dictionary<Guid, string>();

        if (credits.Count > 0)
        {
            var transactionIds = credits.Select(c => c.TransactionId).Distinct().ToList();
            referenceById = await db.CompensationTransactions
                .Where(t => transactionIds.Contains(t.Id))
                .Select(t => new { t.Id, t.ReferenceNumber })
                .ToDictionaryAsync(t => t.Id, t => t.ReferenceNumber, cancellationToken);

            var planIds = credits.Select(c => c.PlanId).Distinct().ToList();
            planNameById = await db.CompensationPlans
                .Where(p => planIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        }

        // ── Step 4: merge on (payee, currency) ────────────────────────────────────────────────────────
        // One open account per currency, because Wasnie holds no exchange rates and must never present a
        // single blended figure. A payee owing EUR and owed USD legitimately appears twice.
        var payeeById = terminated.ToDictionary(p => p.Id);
        var rows = new Dictionary<(Guid PayeeId, string Currency), RowAccumulator>();

        RowAccumulator Row(Guid payeeId, string currency)
        {
            var key = (payeeId, currency);
            if (!rows.TryGetValue(key, out var row))
            {
                var payee = payeeById[payeeId];
                row = new RowAccumulator(payee.Id, payee.FullName, payee.EmployeeCode,
                    payee.TerminationDate, currency, payee.AccountClosedAt);
                rows[key] = row;
            }

            return row;
        }

        foreach (var balance in balances)
        {
            var row = Row(balance.PayeeId, balance.Currency);
            row.Balance = balance.Amount;
            row.BalanceUpdatedAt = balance.UpdatedAt;
        }

        foreach (var credit in credits)
        {
            var row = Row(credit.PayeeId, credit.CreditedAmount.Currency);
            row.Credits.Add(new UnsettledCreditDto(
                CreditId: credit.Id,
                Amount: credit.CreditedAmount.Amount,
                Currency: credit.CreditedAmount.Currency,
                PlanName: planNameById.GetValueOrDefault(credit.PlanId)
                          ?? credit.RuleSnapshot.PlanId.ToString("N")[..8],
                RuleName: credit.RuleSnapshot.RuleName,
                AllocatedAt: DateOnly.FromDateTime(credit.AllocatedAt.UtcDateTime),
                TransactionId: credit.TransactionId,
                TransactionReference: referenceById.GetValueOrDefault(credit.TransactionId)
                                      ?? credit.TransactionId.ToString("N")[..8]));
        }

        // Deepest debt first — the largest exposure is the first thing seen. Rows carrying only unsettled
        // commission have a balance of 0 and sort after the debts, ordered by how much is owed.
        var ordered = rows.Values
            .OrderBy(r => r.Balance)
            .ThenByDescending(r => r.Credits.Sum(c => c.Amount))
            .Select(r => new TerminatedPayeeBalanceDto(
                r.PayeeId,
                r.PayeeName,
                r.EmployeeCode,
                r.TerminationDate,
                r.Balance,
                r.Currency,
                r.BalanceUpdatedAt,
                r.AccountClosedAt,
                r.Credits.Sum(c => c.Amount),
                r.Credits
                    .OrderByDescending(c => c.AllocatedAt)
                    .ToList()))
            .ToList();

        // ★ ONE TOTAL PER CURRENCY, AND ONLY FOR THE CREDITS. The balances carry both signs; adding a
        // debt to a liability produces a number that describes neither. See TerminatedAccountsTotalDto.
        var totals = ordered
            .Where(r => r.UnsettledCredits.Count > 0)
            .GroupBy(r => r.Currency)
            .Select(g => new TerminatedAccountsTotalDto(
                Currency: g.Key,
                UnsettledCreditTotal: g.Sum(r => r.UnsettledCreditTotal),
                UnsettledCreditCount: g.Sum(r => r.UnsettledCredits.Count),
                PayeeCount: g.Select(r => r.PayeeId).Distinct().Count()))
            .OrderByDescending(t => t.UnsettledCreditTotal)
            .ToList();

        return Result<TerminatedAccountsDto>.Success(new TerminatedAccountsDto(ordered, totals));
    }

    /// <summary>
    /// A row under construction. Mutable on purpose: it is filled from two sources that neither know
    /// nor need to know about each other, and either one alone is enough to put the row on the list.
    /// </summary>
    private sealed class RowAccumulator(
        Guid payeeId, string payeeName, string employeeCode, DateOnly? terminationDate, string currency,
        DateTimeOffset? accountClosedAt)
    {
        public Guid PayeeId { get; } = payeeId;
        public string PayeeName { get; } = payeeName;
        public string EmployeeCode { get; } = employeeCode;
        public DateOnly? TerminationDate { get; } = terminationDate;
        public string Currency { get; } = currency;

        /// <summary>Non-null means this row CAME BACK: the account was closed and money arrived after.</summary>
        public DateTimeOffset? AccountClosedAt { get; } = accountClosedAt;

        /// <summary>Stays 0 when there is no ledger balance row — the ordinary case for a payee whose
        /// only open item is unpaid commission.</summary>
        public decimal Balance { get; set; }

        public DateTimeOffset? BalanceUpdatedAt { get; set; }

        public List<UnsettledCreditDto> Credits { get; } = [];
    }
}
