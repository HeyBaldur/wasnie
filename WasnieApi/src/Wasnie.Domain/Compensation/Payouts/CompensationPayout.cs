using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Payouts;

public sealed class CompensationPayout : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid? PayRunId { get; private set; }
    public Guid PayeeId { get; private set; }
    public Guid PlanId { get; private set; }
    public PayeeReference PayeeSnapshot { get; private set; } = null!;
    public DateRange Period { get; private set; } = null!;
    public Money TotalCommission { get; private set; } = null!;
    public CompensationPayoutStatus Status { get; private set; } = CompensationPayoutStatus.Calculated;

    /// <summary>
    /// The instant the money actually left — the cash event, not the compensation period it covers.
    /// Null until paid.
    ///
    /// INVARIANT: PaidAt.HasValue ⟺ Status == Paid. It is stamped in <see cref="MarkPaid"/> and cleared
    /// in <see cref="RevertPaidToApproved"/>, the only two transitions into and out of Paid.
    ///
    /// This is what the treasury/cash-flow reporting attributes to a month. Do NOT substitute Period.End
    /// (the cycle close) — a payout covering 2026-01-01→2026-12-31 closes in December but its money may
    /// have moved in July, and reporting it in December is simply false. Do NOT substitute the underlying
    /// transaction dates either: those would make already-published historical reports mutate whenever a
    /// back-dated transaction is added.
    /// </summary>
    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset CalculatedAt { get; private set; }
    public string CalculatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private readonly List<PayoutLine> _lines = [];
    public IReadOnlyList<PayoutLine> Lines => _lines.AsReadOnly();

    private CompensationPayout() { }

    public static CompensationPayout Calculate(
        Guid tenantId,
        Guid payeeId,
        Guid planId,
        PayeeReference payeeSnapshot,
        DateRange period,
        IReadOnlyList<PayoutLineSpec> lineSpecs,
        string fallbackCurrency,
        string calculatedBy,
        Guid id,
        DateTimeOffset now,
        Guid eventId,
        Func<Guid> newId)
    {
        if (string.IsNullOrWhiteSpace(fallbackCurrency))
            throw new DomainException(
                "A determinable currency is required to calculate a payout. " +
                "Ensure the plan has a currency configured.");

        // Always create a new Money instance — never reuse a spec's reference, which may be
        // an owned-type instance already tracked by EF Core on a different aggregate.
        var totalCommission = lineSpecs.Count > 0
            ? Money.Of(
                lineSpecs.Sum(l => l.CommissionAmount.Amount),
                lineSpecs[0].CommissionAmount.Currency)
            : Money.Zero(fallbackCurrency);

        var payout = new CompensationPayout
        {
            Id = id,
            TenantId = tenantId,
            PayeeId = payeeId,
            PlanId = planId,
            PayeeSnapshot = payeeSnapshot,
            Period = period,
            TotalCommission = totalCommission,
            Status = CompensationPayoutStatus.Calculated,
            CalculatedAt = now,
            CalculatedBy = calculatedBy,
            UpdatedAt = now,
            UpdatedBy = calculatedBy
        };

        foreach (var spec in lineSpecs)
        {
            payout._lines.Add(PayoutLine.Create(
                payout.Id,
                spec.CreditId,
                spec.RuleId,
                spec.RuleName,
                spec.BaseAmount,
                spec.CommissionAmount,
                spec.AppliedModifiers,
                newId()));
        }

        payout.RaiseDomainEvent(new PayoutCalculatedEvent(
            eventId, now, payout.Id, payeeId, tenantId));

        return payout;
    }

    public void Approve(string updatedBy, DateTimeOffset now, Guid eventId)
    {
        if (Status != CompensationPayoutStatus.Calculated)
        {
            throw new DomainException("Only Calculated payouts can be approved.");
        }

        Status = CompensationPayoutStatus.Approved;
        UpdatedAt = now;
        UpdatedBy = updatedBy;

        RaiseDomainEvent(new PayoutApprovedEvent(eventId, now, Id, TenantId));
    }

    public void MarkPaid(string updatedBy, DateTimeOffset now)
    {
        if (Status != CompensationPayoutStatus.Approved)
        {
            throw new DomainException("Only Approved payouts can be marked as Paid.");
        }

        Status = CompensationPayoutStatus.Paid;
        // Stamp the cash event. Immutability while Paid is enforced structurally by the guard above:
        // MarkPaid is the ONLY writer of PaidAt, and it cannot run on an already-Paid payout, so no
        // later operation can move the date of money that has already gone out.
        PaidAt = now;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Associates this payout with a PayRun. Called by the engine after creating or re-running a payout.
    /// Idempotent when called with the same payRunId.
    /// </summary>
    public void AssignToRun(Guid payRunId)
    {
        if (PayRunId.HasValue && PayRunId.Value != payRunId)
            throw new DomainException(
                $"Payout {Id} is already assigned to a different PayRun ({PayRunId.Value}).");
        PayRunId = payRunId;
    }

    /// <summary>
    /// Reverts an Approved payout back to Calculated when its PayRun is reopened.
    /// Only valid for Approved status — Paid and Disputed are not reverted.
    /// </summary>
    public void RevertToCalculated(string updatedBy, DateTimeOffset now)
    {
        if (Status != CompensationPayoutStatus.Approved)
            throw new DomainException("Only Approved payouts can be reverted to Calculated.");

        Status = CompensationPayoutStatus.Calculated;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    // Revert a Paid payout to Approved so its credits can be unconsumed and the period
    // recalculated. Caller is responsible for unconsuming credits and reverting transactions.
    public void RevertPaidToApproved(string updatedBy, DateTimeOffset now)
    {
        if (Status != CompensationPayoutStatus.Paid)
            throw new DomainException("Only Paid payouts can be reverted to Approved.");

        Status = CompensationPayoutStatus.Approved;
        // The payment is annulled, not amended: this handler also unconsumes every credit and returns the
        // transactions to Calculated. A PaidAt left behind would keep claiming cash moved on a day it was
        // taken back, so cash-flow reporting would double count it against the eventual re-payment. If the
        // payout is paid again later, MarkPaid stamps the NEW date — which is when the money really left.
        PaidAt = null;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public void Dispute(string updatedBy, DateTimeOffset now)
    {
        if (Status == CompensationPayoutStatus.Paid)
        {
            throw new DomainException("Paid payouts cannot be disputed.");
        }

        Status = CompensationPayoutStatus.Disputed;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }
}
