using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Compensation.Calculation;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payouts;

namespace Wasnie.Application.Compensation.Calculation;

public interface IPayRunSettlementService
{
    /// <summary>
    /// Withholds outstanding debt from the payouts of a run that is being paid, and stages the
    /// resulting ledger entries, balance updates and settlement rows.
    ///
    /// CONTRACT: this method NEVER calls SaveChanges. Everything it writes must land in the SAME
    /// SaveChanges as <c>Credit.Consume()</c> in the caller — that single atomic write is what
    /// prevents a credit from being consumed while its clawback settlement is lost, or the reverse.
    /// </summary>
    Task<IReadOnlyList<PayRunSettlement>> StageSettlementsAsync(
        Guid tenantId,
        Guid payRunId,
        IReadOnlyList<CompensationPayout> payouts,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>
/// The point where the clawback ledger meets a real payment.
///
/// It runs at MarkPaid, not at calculation, for two reasons that both cost money if ignored:
///   • the calculation engine deletes and recreates Calculated payouts on every re-run
///     (CalculatePayoutsForPeriodHandler), so a ledger write there would duplicate on each re-run;
///   • a payout that is calculated but never paid must not reduce anyone's debt.
///
/// It also closes the exact-period gap in the Approved/Paid block (a narrower re-run can put the
/// same credit in two payouts): settlement is bound to the payment, and the payment is already
/// guarded against consuming a credit twice.
/// </summary>
public sealed class PayRunSettlementService(IApplicationDbContext db, IGuidGenerator guid)
    : IPayRunSettlementService
{
    public async Task<IReadOnlyList<PayRunSettlement>> StageSettlementsAsync(
        Guid tenantId,
        Guid payRunId,
        IReadOnlyList<CompensationPayout> payouts,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var settlements = new List<PayRunSettlement>();
        if (payouts.Count == 0) return settlements;

        var payeeIds = payouts.Select(p => p.PayeeId).Distinct().ToList();

        // Tracked on purpose: the balance is mutated and its RowVersion must take part in the
        // caller's SaveChanges, so a manual adjustment landing mid-payment fails loudly.
        var balances = await db.PayeeBalances
            .IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && payeeIds.Contains(b.PayeeId))
            .ToListAsync(cancellationToken);

        if (balances.Count == 0) return settlements;

        var planIds = payouts.Select(p => p.PlanId).Distinct().ToList();
        var capByPlanId = (await db.CompensationPlans
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId && planIds.Contains(p.Id))
                .Select(p => new { p.Id, p.ClawbackCapPercent })
                .ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, p => p.ClawbackCapPercent);

        // One settlement per (payee, currency) — a payee paid in EUR and USD settles each balance
        // against the payouts of that same currency and never across.
        var groups = payouts
            .GroupBy(p => (p.PayeeId, Currency: p.TotalCommission.Currency));

        foreach (var group in groups)
        {
            var balance = balances.FirstOrDefault(
                b => b.PayeeId == group.Key.PayeeId && b.Currency == group.Key.Currency);

            var debt = balance?.OutstandingDebt();
            if (balance is null || debt is null || debt.Amount <= 0m)
                continue; // nothing owed in this currency — the payout is paid in full

            var forSettlement = group
                .Select(p => new PayoutForSettlement(
                    p.Id, p.PlanId, p.TotalCommission,
                    capByPlanId.TryGetValue(p.PlanId, out var cap) ? cap : null))
                .ToList();

            var result = PayeeSettlementCalculator.Settle(forSettlement, debt);
            if (result.TotalWithheld.Amount <= 0m)
                continue; // every plan's cap protected this period's commissions in full

            var gross = forSettlement
                .Skip(1)
                .Aggregate(forSettlement[0].Gross, (acc, p) => acc.Add(p.Gross));

            var entry = PayeeLedgerEntry.CreateSystemEntry(
                tenantId,
                group.Key.PayeeId,
                LedgerTransactionType.ClawbackAppliedCredit,
                result.TotalWithheld,
                $"Withheld from pay run {payRunId} against an outstanding balance of {debt}.",
                LedgerSourceType.PayRunSettlement,
                actor,
                guid.NewGuid(),
                now,
                guid.NewGuid(),
                sourcePayRunId: payRunId);

            // Apply BEFORE staging: if the currencies disagree this throws and nothing is written.
            balance.Apply(entry, now);
            db.PayeeLedgerEntries.Add(entry);

            var settlement = PayRunSettlement.Record(
                tenantId, payRunId, group.Key.PayeeId,
                gross, result.TotalWithheld, result.Carryover,
                entry.Id, actor, guid.NewGuid(), now);

            db.PayRunSettlements.Add(settlement);
            settlements.Add(settlement);
        }

        return settlements;
    }
}
