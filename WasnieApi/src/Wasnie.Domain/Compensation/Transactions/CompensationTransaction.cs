using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Transactions;

public sealed class CompensationTransaction : AggregateRoot
{
    private static readonly DateOnly MinTransactionDate = new DateOnly(2000, 1, 1);

    public Guid TenantId { get; private set; }
    public string ReferenceNumber { get; private set; } = string.Empty;
    public Guid PayeeId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public DateOnly TransactionDate { get; private set; }
    public TransactionSource Source { get; private set; }
    public CompensationTransactionStatus Status { get; private set; } = CompensationTransactionStatus.Pending;
    public string? ExternalId { get; private set; }
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
        Guid id,
        DateTimeOffset now,
        Guid eventId,
        string? externalId = null)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");
        if (string.IsNullOrWhiteSpace(referenceNumber))
            throw new DomainException("Reference number is required.");
        if (payeeId == Guid.Empty)
            throw new DomainException("PayeeId must not be empty.");
        if (transactionDate < MinTransactionDate)
            throw new DomainException($"Transaction date cannot be before {MinTransactionDate:yyyy-MM-dd}.");
        if (string.IsNullOrEmpty(ingestedBy))
            throw new DomainException("IngestedBy is required.");

        var tx = new CompensationTransaction
        {
            Id = id,
            TenantId = tenantId,
            ReferenceNumber = referenceNumber,
            PayeeId = payeeId,
            Amount = amount,
            TransactionDate = transactionDate,
            Source = source,
            Status = CompensationTransactionStatus.Pending,
            ExternalId = externalId,
            IngestedAt = now,
            IngestedBy = ingestedBy,
            UpdatedAt = now
        };

        tx.RaiseDomainEvent(new TransactionIngestedEvent(
            eventId, now, tx.Id, tenantId, referenceNumber));

        return tx;
    }

    // Pending → Eligible: transaction validated and ready for the calculation engine.
    public void MarkEligible(string updatedBy, DateTimeOffset now, Guid eventId)
    {
        if (Status != CompensationTransactionStatus.Pending)
            throw new DomainException("Only Pending transactions can be marked Eligible.");

        Status = CompensationTransactionStatus.Eligible;
        UpdatedAt = now;

        RaiseDomainEvent(new TransactionMarkedEligibleEvent(eventId, now, Id, TenantId));
    }

    // Phase 3 stub — implemented when the calculation engine is built.
    public void MarkCalculated(string updatedBy, DateTimeOffset now, Guid eventId)
        => throw new NotSupportedException("MarkCalculated is implemented in Phase 3.");

    // Phase 3 stub — implemented when the payout module is built.
    public void MarkPaid(string updatedBy, DateTimeOffset now, Guid eventId)
        => throw new NotSupportedException("MarkPaid is implemented in Phase 3.");

    // Pending → Cancelled, Eligible → Cancelled.
    // Cancellation of Calculated/Paid requires clawback evaluation (Phase 3).
    public void Cancel(string updatedBy, DateTimeOffset now, Guid eventId)
    {
        if (Status == CompensationTransactionStatus.Cancelled)
            throw new DomainException("Transaction is already cancelled.");

        if (Status is CompensationTransactionStatus.Calculated or CompensationTransactionStatus.Paid)
            throw new DomainException($"Cannot cancel a {Status} transaction — clawback evaluation required (Phase 3).");

        Status = CompensationTransactionStatus.Cancelled;
        UpdatedAt = now;

        RaiseDomainEvent(new TransactionCancelledEvent(eventId, now, Id, TenantId));
    }
}
