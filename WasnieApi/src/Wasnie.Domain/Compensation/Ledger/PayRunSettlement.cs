using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Ledger;

/// <summary>
/// What a payee was ACTUALLY paid after outstanding debt was withheld, for one pay run and one
/// currency. Written once, when the run is marked Paid — never at calculation time.
///
/// Why this exists instead of adjusting the payout: automatic calculations are immutable. The
/// payout keeps saying what the plan rules earned (gross), and this row says what left the company
/// after the ledger took its share. Two different facts, both true, neither overwriting the other.
/// The payroll figure is <see cref="NetPaid"/>.
///
/// It is also why the calculation engine can keep deleting and recreating Calculated payouts on a
/// re-run (CalculatePayoutsForPeriodHandler) without ever touching settlement history.
/// </summary>
public sealed class PayRunSettlement : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid PayRunId { get; private set; }
    public Guid PayeeId { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Sum of the payee's payouts in this run and currency, as calculated by the plan rules.</summary>
    public Money GrossCommission { get; private set; } = null!;

    /// <summary>Positive amount withheld against outstanding debt. Never exceeds the plans' caps.</summary>
    public Money ClawbackWithheld { get; private set; } = null!;

    /// <summary>Gross − withheld. This is the number that goes to payroll.</summary>
    public Money NetPaid { get; private set; } = null!;

    /// <summary>Debt still owed after this run — the carryover into the next period.</summary>
    public Money CarryoverRemaining { get; private set; } = null!;

    /// <summary>The ClawbackAppliedCredit entry that moved the balance. Null when nothing was withheld.</summary>
    public Guid? LedgerEntryId { get; private set; }

    public DateTimeOffset AppliedAt { get; private set; }
    public string AppliedBy { get; private set; } = string.Empty;

    private PayRunSettlement() { }

    public static PayRunSettlement Record(
        Guid tenantId,
        Guid payRunId,
        Guid payeeId,
        Money grossCommission,
        Money clawbackWithheld,
        Money carryoverRemaining,
        Guid? ledgerEntryId,
        string appliedBy,
        Guid id,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty) throw new DomainException("TenantId must not be empty.");
        if (payRunId == Guid.Empty) throw new DomainException("PayRunId must not be empty.");
        if (payeeId == Guid.Empty) throw new DomainException("PayeeId must not be empty.");
        if (string.IsNullOrWhiteSpace(appliedBy)) throw new DomainException("AppliedBy is required.");
        if (clawbackWithheld.Amount < 0m)
            throw new DomainException("Withheld amount is expressed as a positive figure.");
        if (clawbackWithheld.Amount > grossCommission.Amount)
            throw new DomainException(
                $"Cannot withhold {clawbackWithheld} from a gross commission of {grossCommission} — " +
                "a settlement may never produce a negative payment.");

        return new PayRunSettlement
        {
            Id = id,
            TenantId = tenantId,
            PayRunId = payRunId,
            PayeeId = payeeId,
            Currency = grossCommission.Currency,
            GrossCommission = grossCommission,
            ClawbackWithheld = clawbackWithheld,
            // Subtract guards the currency, so a mismatched pair throws before a wrong figure is stored.
            NetPaid = grossCommission.Subtract(clawbackWithheld),
            CarryoverRemaining = carryoverRemaining,
            LedgerEntryId = ledgerEntryId,
            AppliedAt = now,
            AppliedBy = appliedBy,
        };
    }
}
