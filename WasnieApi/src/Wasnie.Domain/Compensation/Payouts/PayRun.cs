using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Payouts;

public sealed class PayRun : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public PayRunStatus Status { get; private set; } = PayRunStatus.Draft;
    // 0 = primary run for the period; 1, 2, … = supplemental runs created after the primary was Paid/Approved.
    // Enforced unique by (TenantId, PeriodStart, PeriodEnd, SupplementalSequence) in DB.
    public int SupplementalSequence { get; private set; } = 0;

    // Lifecycle audit — Rule 10: every status write records actor + timestamp via IClock.
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset? ApprovedAt { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public string? PaidBy { get; private set; }

    // Denormalized roll-ups — recomputed on each (re)calculation via UpdateRollUps().
    // PayoutLines remain the source of truth. These are a read optimisation.
    // CARTESIAN GUARD: populated by UpdateRollUps() which iterates one flat list — no joins.
    public int PayeeCount { get; private set; }
    public int PaidPayeeCount { get; private set; }
    public int ZeroPayoutCount { get; private set; }

    // Per-currency totals, e.g. { "EUR": 95000.00, "PLN": 87500.00 }.
    // Stored as JSON in the DB. $0 payees are excluded from TotalAmounts (Decision 5).
    public Dictionary<string, decimal> TotalAmounts { get; private set; } =
        new(StringComparer.OrdinalIgnoreCase);

    private PayRun() { }

    /// <summary>
    /// Creates a new PayRun in Draft status for the given period.
    /// Unique constraint is (TenantId, PeriodStart, PeriodEnd, SupplementalSequence) in DB.
    /// Pass supplementalSequence = 0 for the primary run; use max+1 for supplemental runs.
    /// </summary>
    public static PayRun Open(
        Guid tenantId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string createdBy,
        Guid id,
        DateTimeOffset now,
        int supplementalSequence = 0)
    {
        if (periodStart > periodEnd)
            throw new DomainException("PeriodStart must be on or before PeriodEnd.");
        if (string.IsNullOrWhiteSpace(createdBy))
            throw new DomainException("CreatedBy is required.");
        if (supplementalSequence < 0)
            throw new DomainException("SupplementalSequence must be zero or positive.");

        return new PayRun
        {
            Id = id,
            TenantId = tenantId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Status = PayRunStatus.Draft,
            CreatedAt = now,
            CreatedBy = createdBy,
            SupplementalSequence = supplementalSequence,
        };
    }

    /// <summary>Draft → Approved. Cascading individual payout transitions is the handler's responsibility.</summary>
    public void Approve(string actor, DateTimeOffset now, Guid eventId)
    {
        if (Status != PayRunStatus.Draft)
            throw new DomainException("Only a Draft pay run can be approved.");
        if (string.IsNullOrWhiteSpace(actor))
            throw new DomainException("Actor is required.");

        Status = PayRunStatus.Approved;
        ApprovedAt = now;
        ApprovedBy = actor;

        RaiseDomainEvent(new PayRunApprovedEvent(eventId, now, Id, TenantId));
    }

    /// <summary>
    /// Approved → Paid. Irreversible. Once Paid the period is locked (Decision 4).
    /// Corrections to a Paid run are handled via future Adjustment WI.
    /// </summary>
    public void MarkPaid(string actor, DateTimeOffset now, Guid eventId)
    {
        if (Status != PayRunStatus.Approved)
            throw new DomainException("Only an Approved pay run can be marked as Paid.");
        if (string.IsNullOrWhiteSpace(actor))
            throw new DomainException("Actor is required.");

        Status = PayRunStatus.Paid;
        PaidAt = now;
        PaidBy = actor;

        RaiseDomainEvent(new PayRunPaidEvent(eventId, now, Id, TenantId));
    }

    /// <summary>
    /// Approved → Draft. Allowed only while NOT Paid (Decision 3).
    /// Reopen is always run-wide — all payouts revert. Partial reopen is not allowed.
    /// ApprovedAt/By are cleared to signal the approval gate is open again.
    /// </summary>
    public void Reopen(string actor, DateTimeOffset now, Guid eventId)
    {
        if (Status != PayRunStatus.Approved)
            throw new DomainException(
                "Only an Approved pay run can be reopened. A Paid run cannot be reopened.");
        if (string.IsNullOrWhiteSpace(actor))
            throw new DomainException("Actor is required.");

        Status = PayRunStatus.Draft;
        ApprovedAt = null;
        ApprovedBy = null;

        RaiseDomainEvent(new PayRunReopenedEvent(eventId, now, Id, TenantId));
    }

    /// <summary>
    /// Recomputes all denormalized roll-ups from the run's current payout snapshot.
    /// Call after every (re)calculation and after run-level transitions that change payout statuses.
    ///
    /// Rule 2 compliance: pure function — inputs are in-memory collections, no IO side effects.
    /// CARTESIAN GUARD: iterates <paramref name="payouts"/> once, no joins, no sub-queries.
    /// </summary>
    public void UpdateRollUps(IReadOnlyList<CompensationPayout> payouts)
    {
        PayeeCount = payouts.Count;
        PaidPayeeCount = payouts.Count(p => p.TotalCommission.Amount > 0m);
        ZeroPayoutCount = PayeeCount - PaidPayeeCount;

        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var payout in payouts)
        {
            if (payout.TotalCommission.Amount <= 0m) continue;
            var cur = payout.TotalCommission.Currency;
            totals[cur] = totals.TryGetValue(cur, out var existing)
                ? existing + payout.TotalCommission.Amount
                : payout.TotalCommission.Amount;
        }
        TotalAmounts = totals;
    }
}
