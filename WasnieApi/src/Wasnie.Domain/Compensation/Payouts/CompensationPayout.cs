using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Payouts;

public sealed class CompensationPayout : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid PayeeId { get; private set; }
    public Guid PlanId { get; private set; }
    public PayeeReference PayeeSnapshot { get; private set; } = null!;
    public DateRange Period { get; private set; } = null!;
    public Money TotalCommission { get; private set; } = null!;
    public CompensationPayoutStatus Status { get; private set; } = CompensationPayoutStatus.Calculated;
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
