using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Calculation;

/// <summary>
/// How much of an already-PAID commission comes back. Pure: no DB, no clock, no I/O — the caller
/// supplies the facts and gets a number it can reproduce.
///
/// The trigger decides the METHOD, not this type:
///   • churn (a real deal that died inside its maturation window) → <see cref="Proportional"/>;
///   • origin error (the contract was never real)                → <see cref="Full"/>, 100%.
/// </summary>
public static class ClawbackCalculator
{
    /// <summary>
    /// Churn: the payee keeps the fraction of the commission they "earned" by keeping the deal alive.
    ///
    ///     clawback = paid × (maturationDays − daysActive) / maturationDays,  floored at 0
    ///
    /// Written as one multiplication then one division on purpose: computing the ratio first
    /// (1 − 30/90 = 0.6666…) rounds before it multiplies, which loses cents on large commissions.
    /// This form keeps 900 × 60 / 90 at exactly 600.00.
    ///
    /// The floor at zero is the rule that matters in someone's pay: a deal that outlived its window
    /// gives back NOTHING — it can never turn into a bonus.
    /// </summary>
    public static Money Proportional(Money commissionPaid, int daysActive, int maturationDays)
    {
        if (commissionPaid is null)
            throw new DomainException("Commission paid is required to compute a clawback.");
        if (commissionPaid.Amount < 0m)
            throw new DomainException("Commission paid must not be negative.");
        if (maturationDays <= 0)
            throw new DomainException("Maturation days must be greater than zero.");
        if (daysActive < 0)
            throw new DomainException(
                "Days active must not be negative — a deal cannot churn before it was won.");

        var unearnedDays = maturationDays - daysActive;
        if (unearnedDays <= 0)
            return Money.Zero(commissionPaid.Currency);

        return commissionPaid.Multiply(unearnedDays).Divide(maturationDays);
    }

    /// <summary>
    /// Origin error: the sale never existed, so the whole commission comes back. Not proportional —
    /// there is no "time earned" on a contract that was never real.
    /// </summary>
    public static Money Full(Money commissionPaid)
    {
        if (commissionPaid is null)
            throw new DomainException("Commission paid is required to compute a clawback.");
        if (commissionPaid.Amount < 0m)
            throw new DomainException("Commission paid must not be negative.");

        return commissionPaid;
    }

    /// <summary>
    /// Days the deal stayed won: from the transaction (close) date to the churn date, floored at 0.
    /// A churn date before the close date means bad CRM data, not a negative lifetime — treated as
    /// zero days active, i.e. the full clawback.
    /// </summary>
    public static int DaysActiveBetween(DateOnly closedOn, DateOnly churnedOn)
    {
        var days = churnedOn.DayNumber - closedOn.DayNumber;
        return days < 0 ? 0 : days;
    }
}
