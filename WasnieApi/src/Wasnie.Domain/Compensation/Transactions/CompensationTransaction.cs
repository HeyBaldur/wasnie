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
    // Nullable per Decision D: e-commerce/house-pool/system-return rows may have no payee at ingest.
    // Phase 3 filter: PayeeId IS NOT NULL AND Status = Pending to find processable transactions.
    public Guid? PayeeId { get; private set; }
    public Money Amount { get; private set; } = null!;
    // Number of units represented by this transaction (WI-PROD-QUANTITY-FIELD).
    // Default 1 — single-line sales. Set > 1 for multi-item transactions (e.g. 5 units in one POS row).
    public int Quantity { get; private set; } = 1;
    public DateOnly TransactionDate { get; private set; }
    public TransactionSource Source { get; private set; }
    public CompensationTransactionStatus Status { get; private set; } = CompensationTransactionStatus.Pending;
    public string? ExternalId { get; private set; }
    public DateTimeOffset IngestedAt { get; private set; }
    public string IngestedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    // Cancellation audit — populated only when Status == Cancelled
    public string? CancelledBy { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelledReason { get; private set; }

    private CompensationTransaction() { }

    public static CompensationTransaction Ingest(
        Guid tenantId,
        string referenceNumber,
        Guid? payeeId,
        Money amount,
        DateOnly transactionDate,
        TransactionSource source,
        string ingestedBy,
        Guid id,
        DateTimeOffset now,
        Guid eventId,
        string? externalId = null,
        int quantity = 1)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");
        if (string.IsNullOrWhiteSpace(referenceNumber))
            throw new DomainException("Reference number is required.");
        if (payeeId.HasValue && payeeId.Value == Guid.Empty)
            throw new DomainException("PayeeId must not be empty when provided.");
        if (transactionDate < MinTransactionDate)
            throw new DomainException($"Transaction date cannot be before {MinTransactionDate:yyyy-MM-dd}.");
        if (string.IsNullOrEmpty(ingestedBy))
            throw new DomainException("IngestedBy is required.");
        if (quantity < 1)
            throw new DomainException("Quantity must be at least 1.");

        var tx = new CompensationTransaction
        {
            Id = id,
            TenantId = tenantId,
            ReferenceNumber = referenceNumber,
            PayeeId = payeeId,
            Amount = amount,
            Quantity = quantity,
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

    // Pending → Calculated: transaction has had Credits allocated by the calculation engine.
    public void MarkCalculated(int creditCount, Money totalCommission, string updatedBy, DateTimeOffset now, Guid eventId)
    {
        if (Status != CompensationTransactionStatus.Pending)
            throw new DomainException($"Only Pending transactions can be marked Calculated. Current status: {Status}.");

        Status = CompensationTransactionStatus.Calculated;
        UpdatedAt = now;

        RaiseDomainEvent(new TransactionCalculatedEvent(
            eventId, now, Id, PayeeId ?? Guid.Empty, TenantId,
            creditCount, totalCommission.Amount, totalCommission.Currency));
    }

    // Phase 3 stub — implemented when the payout module is built.
    public void MarkPaid(string updatedBy, DateTimeOffset now, Guid eventId)
        => throw new NotSupportedException("MarkPaid is implemented in Phase 3.");

    // Assign a payee to a previously unassigned transaction (PayeeId IS NULL).
    // State rules per Decision 11: Paid is blocked; Eligible/Calculated → revert to Pending.
    public void Assign(Guid payeeId, string? comment, string updatedBy, DateTimeOffset now, Guid eventId)
    {
        if (Status == CompensationTransactionStatus.Paid)
            throw new DomainException("Cannot assign a payee to a Paid transaction — please use the accounting correction workflow.");
        if (PayeeId.HasValue)
            throw new DomainException("Transaction already has an assigned payee. Use ReassignPayeeCommand to change it.");
        if (payeeId == Guid.Empty)
            throw new DomainException("PayeeId must not be empty.");

        PayeeId = payeeId;
        RevertToPendingIfNeeded(updatedBy, now);
        UpdatedAt = now;

        RaiseDomainEvent(new TransactionPayeeAssignedEvent(eventId, now, Id, TenantId, payeeId, comment));
    }

    // Reassign from one payee to another (PayeeId IS NOT NULL → new value).
    // Reason is mandatory (min 10 chars) and persisted in the audit event (Decision 11).
    public void Reassign(Guid newPayeeId, string reason, string updatedBy, DateTimeOffset now, Guid eventId)
    {
        if (Status == CompensationTransactionStatus.Paid)
            throw new DomainException("Cannot reassign a Paid transaction — please use the accounting correction workflow.");
        if (!PayeeId.HasValue)
            throw new DomainException("Transaction has no assigned payee. Use AssignPayeeCommand to assign one.");
        if (newPayeeId == Guid.Empty)
            throw new DomainException("NewPayeeId must not be empty.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
            throw new DomainException("Reassignment reason is required and must be at least 10 characters.");

        var oldPayeeId = PayeeId.Value;
        PayeeId = newPayeeId;
        RevertToPendingIfNeeded(updatedBy, now);
        UpdatedAt = now;

        RaiseDomainEvent(new TransactionPayeeReassignedEvent(eventId, now, Id, TenantId, oldPayeeId, newPayeeId, reason.Trim()));
    }

    // Reverts Status to Pending when PayeeId changes on an Eligible or Calculated transaction.
    // Decision 11: assignment change invalidates prior eligibility/calculation results.
    private void RevertToPendingIfNeeded(string updatedBy, DateTimeOffset now)
    {
        if (Status is CompensationTransactionStatus.Eligible or CompensationTransactionStatus.Calculated)
        {
            Status = CompensationTransactionStatus.Pending;
        }
    }

    // Apply value changes from the Excel re-upload workflow (WI-PROD-T).
    // The caller MUST supersede existing Credits before calling this when Status == Calculated.
    // Paid transactions MUST be rejected before reaching this method.
    public void ApplyExcelUpdate(
        Money? newAmount,
        int? newQuantity,
        DateOnly? newDate,
        Guid? newPayeeId,
        string updatedBy,
        DateTimeOffset now)
    {
        if (Status == CompensationTransactionStatus.Paid)
            throw new DomainException("Cannot update a Paid transaction via Excel re-upload.");

        if (newAmount is not null)
            Amount = newAmount;

        if (newQuantity.HasValue)
        {
            if (newQuantity.Value < 1)
                throw new DomainException("Quantity must be at least 1.");
            Quantity = newQuantity.Value;
        }

        if (newDate.HasValue)
        {
            if (newDate.Value < MinTransactionDate)
                throw new DomainException($"Transaction date cannot be before {MinTransactionDate:yyyy-MM-dd}.");
            TransactionDate = newDate.Value;
        }

        if (newPayeeId.HasValue)
            PayeeId = newPayeeId.Value;

        // Any value change on a Calculated transaction reverts it to Pending.
        if (Status == CompensationTransactionStatus.Calculated)
            Status = CompensationTransactionStatus.Pending;

        UpdatedAt = now;
    }

    // Pending → Cancelled.
    // Only Pending transactions may be voided; Calculated/Paid require clawback (Phase 3).
    // Reason is mandatory (min 3 chars) and persisted for audit trail.
    public void Cancel(string reason, string cancelledBy, DateTimeOffset now, Guid eventId)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
            throw new DomainException("Cancellation reason is required and must be at least 3 characters.");

        if (Status == CompensationTransactionStatus.Cancelled)
            throw new DomainException("Transaction is already cancelled.");

        if (Status is not CompensationTransactionStatus.Pending)
            throw new DomainException($"Only Pending transactions can be voided. Current status: {Status}.");

        Status = CompensationTransactionStatus.Cancelled;
        CancelledBy = cancelledBy;
        CancelledAt = now;
        CancelledReason = reason.Trim();
        UpdatedAt = now;

        RaiseDomainEvent(new TransactionCancelledEvent(eventId, now, Id, TenantId, reason.Trim()));
    }
}
