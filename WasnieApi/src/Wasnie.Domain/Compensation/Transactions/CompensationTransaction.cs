using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Transactions;

public sealed class CompensationTransaction : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public string ReferenceNumber { get; private set; } = string.Empty;
    public Guid PayeeId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public DateOnly TransactionDate { get; private set; }
    public TransactionSource Source { get; private set; }
    public CompensationTransactionStatus Status { get; private set; } = CompensationTransactionStatus.Pending;
    public string? ExternalReference { get; private set; }
    public DateTimeOffset IngestedAt { get; private set; }
    public string IngestedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    private CompensationTransaction() { }

    public static CompensationTransaction Ingest(
        Guid tenantId,
        string referenceNumber,
        Guid payeeId,
        Money amount,
        DateOnly transactionDate,
        TransactionSource source,
        string ingestedBy,
        string? externalReference = null)
    {
        var tx = new CompensationTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ReferenceNumber = referenceNumber,
            PayeeId = payeeId,
            Amount = amount,
            TransactionDate = transactionDate,
            Source = source,
            Status = CompensationTransactionStatus.Pending,
            ExternalReference = externalReference,
            IngestedAt = DateTimeOffset.UtcNow,
            IngestedBy = ingestedBy,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        tx.RaiseDomainEvent(new TransactionIngestedEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, tx.Id, tenantId, referenceNumber));

        return tx;
    }

    public void Cancel(string updatedBy)
    {
        if (Status == CompensationTransactionStatus.Cancelled)
        {
            throw new DomainException("Transaction is already cancelled.");
        }

        if (Status == CompensationTransactionStatus.Credited)
        {
            throw new DomainException("Cannot cancel a credited transaction.");
        }

        Status = CompensationTransactionStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;

        RaiseDomainEvent(new TransactionCancelledEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Id, TenantId));
    }

    public void MarkCredited()
    {
        if (Status != CompensationTransactionStatus.Pending)
        {
            throw new DomainException("Only Pending transactions can be credited.");
        }

        Status = CompensationTransactionStatus.Credited;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
