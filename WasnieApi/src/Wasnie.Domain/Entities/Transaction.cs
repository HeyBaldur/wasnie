using Wasnie.Domain.Common;
using Wasnie.Domain.Enums;

namespace Wasnie.Domain.Entities;

public sealed class Transaction : BaseAuditableEntity
{
    public string ReferenceNumber { get; private set; } = string.Empty;
    public Guid PayeeId { get; private set; }
    public Guid PlanId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public DateOnly TransactionDate { get; private set; }
    public TransactionStatus Status { get; private set; } = TransactionStatus.Pending;

    private Transaction() { }

    public static Transaction Create(
        Guid tenantId,
        string referenceNumber,
        Guid payeeId,
        Guid planId,
        decimal amount,
        string currency,
        DateOnly transactionDate,
        string createdBy)
    {
        return new Transaction
        {
            TenantId = tenantId,
            ReferenceNumber = referenceNumber,
            PayeeId = payeeId,
            PlanId = planId,
            Amount = amount,
            Currency = currency,
            TransactionDate = transactionDate,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = createdBy
        };
    }

    public void Approve() => Status = TransactionStatus.Approved;
    public void Reject() => Status = TransactionStatus.Rejected;
    public void MarkProcessed() => Status = TransactionStatus.Processed;
}
