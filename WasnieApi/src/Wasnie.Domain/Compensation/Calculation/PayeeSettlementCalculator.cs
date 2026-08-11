using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Calculation;

/// <summary>One payout entering the settlement, with the cap its plan allows to be withheld.</summary>
/// <param name="CapPercent">
/// 0–100, or null for "no ceiling" (the whole payout may be withheld). The cap is per PLAN because
/// that is where the commercial promise lives; the DEBT is per payee, global across plans. So a
/// payee with two plans has their single debt collected from both payouts, each limited by its own
/// plan's ceiling.
/// </param>
public sealed record PayoutForSettlement(Guid PayoutId, Guid PlanId, Money Gross, decimal? CapPercent);

public sealed record PayoutSettlement(Guid PayoutId, Guid PlanId, Money Gross, Money Withheld, Money Net);

public sealed record PayeeSettlementResult(
    IReadOnlyList<PayoutSettlement> Payouts,
    Money TotalWithheld,
    Money Carryover);

/// <summary>
/// Decides how much of an outstanding debt a payee's payouts absorb this period. Pure and
/// deterministic — the same inputs always produce the same cents, in the same order.
///
/// The rule the payee cares about: they NEVER lose more than the cap allows, and whatever the cap
/// prevents from being collected stays as debt for the next period. The carryover is not a separate
/// mechanism — it is simply the part of the balance that was not settled.
/// </summary>
public static class PayeeSettlementCalculator
{
    public static PayeeSettlementResult Settle(
        IReadOnlyList<PayoutForSettlement> payouts, Money outstandingDebt)
    {
        if (payouts is null)
            throw new DomainException("Payouts are required to settle a payee.");
        if (outstandingDebt is null)
            throw new DomainException("Outstanding debt is required to settle a payee.");
        if (outstandingDebt.Amount < 0m)
            throw new DomainException(
                "Outstanding debt is expressed as a positive figure; pass PayeeBalance.OutstandingDebt().");

        var currency = outstandingDebt.Currency;
        foreach (var p in payouts)
        {
            if (p.Gross.Currency != currency)
                throw new DomainException(
                    $"Cannot settle a {p.Gross.Currency} payout against a {currency} balance — " +
                    "settlement is per currency because Incentra holds no exchange rates.");
            if (p.Gross.Amount < 0m)
                throw new DomainException("A payout gross amount must not be negative.");
            if (p.CapPercent is < 0m or > 100m)
                throw new DomainException("The clawback cap must be a percentage between 0 and 100.");
        }

        var remaining = outstandingDebt;
        var zero = Money.Zero(currency);
        var settled = new List<PayoutSettlement>(payouts.Count);

        // Deterministic order: two runs over the same data must withhold from the same payouts in the
        // same sequence, or the same debt would land on a different plan depending on load order.
        foreach (var payout in payouts.OrderBy(p => p.PlanId).ThenBy(p => p.PayoutId))
        {
            var ceiling = payout.CapPercent is null
                ? payout.Gross
                : payout.Gross.Multiply(payout.CapPercent.Value).Divide(100m);

            var withheld = remaining.Amount < ceiling.Amount ? remaining : ceiling;
            if (withheld.Amount < 0m) withheld = zero;

            remaining = remaining.Subtract(withheld);
            settled.Add(new PayoutSettlement(
                payout.PayoutId, payout.PlanId, payout.Gross, withheld, payout.Gross.Subtract(withheld)));
        }

        var totalWithheld = outstandingDebt.Subtract(remaining);
        return new PayeeSettlementResult(settled, totalWithheld, remaining);
    }
}
