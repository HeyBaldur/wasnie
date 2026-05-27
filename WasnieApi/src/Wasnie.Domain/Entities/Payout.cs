using Wasnie.Domain.Common;
using Wasnie.Domain.Enums;

namespace Wasnie.Domain.Entities;

public sealed class Payout : BaseAuditableEntity
{
    public Guid PayeeId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid TransactionId { get; private set; }
    public decimal CommissionAmount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public int PeriodYear { get; private set; }
    public int PeriodMonth { get; private set; }
    public PayoutStatus Status { get; private set; } = PayoutStatus.Calculated;

    private Payout() { }

    public static Payout Create(
        Guid tenantId,
        Guid payeeId,
        Guid planId,
        Guid transactionId,
        decimal commissionAmount,
        string currency,
        int periodYear,
        int periodMonth,
        string createdBy,
        Guid id,
        DateTimeOffset now)
    {
        return new Payout
        {
            Id = id,
            TenantId = tenantId,
            PayeeId = payeeId,
            PlanId = planId,
            TransactionId = transactionId,
            CommissionAmount = commissionAmount,
            Currency = currency,
            PeriodYear = periodYear,
            PeriodMonth = periodMonth,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = createdBy
        };
    }

    public void Approve() => Status = PayoutStatus.Approved;
    public void MarkPaid() => Status = PayoutStatus.Paid;
    public void Dispute() => Status = PayoutStatus.Disputed;
}
