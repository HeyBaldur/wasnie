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
        PayeeReference payeeSnapshot,
        DateRange period,
        IReadOnlyList<PayoutLine> lines,
        string calculatedBy)
    {
        var totalCommission = lines.Count > 0
            ? lines.Skip(1).Aggregate(lines[0].CommissionAmount, (acc, l) => acc.Add(l.CommissionAmount))
            : Money.Zero("USD");

        var payout = new CompensationPayout
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PayeeId = payeeId,
            PayeeSnapshot = payeeSnapshot,
            Period = period,
            TotalCommission = totalCommission,
            Status = CompensationPayoutStatus.Calculated,
            CalculatedAt = DateTimeOffset.UtcNow,
            CalculatedBy = calculatedBy,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = calculatedBy
        };

        foreach (var line in lines)
        {
            payout._lines.Add(PayoutLine.Create(
                payout.Id,
                line.CreditId,
                line.RuleId,
                line.RuleName,
                line.BaseAmount,
                line.CommissionAmount,
                line.AppliedModifiers));
        }

        payout.RaiseDomainEvent(new PayoutCalculatedEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, payout.Id, payeeId, tenantId));

        return payout;
    }

    public void Approve(string updatedBy)
    {
        if (Status != CompensationPayoutStatus.Calculated)
        {
            throw new DomainException("Only Calculated payouts can be approved.");
        }

        Status = CompensationPayoutStatus.Approved;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;

        RaiseDomainEvent(new PayoutApprovedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Id, TenantId));
    }

    public void MarkPaid(string updatedBy)
    {
        if (Status != CompensationPayoutStatus.Approved)
        {
            throw new DomainException("Only Approved payouts can be marked as Paid.");
        }

        Status = CompensationPayoutStatus.Paid;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void Dispute(string updatedBy)
    {
        if (Status == CompensationPayoutStatus.Paid)
        {
            throw new DomainException("Paid payouts cannot be disputed.");
        }

        Status = CompensationPayoutStatus.Disputed;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;
    }
}
