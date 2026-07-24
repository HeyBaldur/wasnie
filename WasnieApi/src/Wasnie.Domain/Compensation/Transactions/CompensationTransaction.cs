using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Transactions;

public sealed class CompensationTransaction : AggregateRoot
{
    private static readonly DateOnly MinTransactionDate = new DateOnly(2000, 1, 1);

    // Max persisted length of Description — mirrors ExternalId/Notes elsewhere in the model.
    public const int MaxDescriptionLength = 500;

    public Guid TenantId { get; private set; }
    public string ReferenceNumber { get; private set; } = string.Empty;
    // Human-readable label for the sale this transaction represents (HubSpot deal name, a manually
    // typed description, or an Excel column). PURELY DESCRIPTIVE — never used in calculation,
    // matching, idempotency or attribution; those all key off ReferenceNumber/ExternalId.
    // Null for transactions ingested before this field existed and whenever no name is supplied.
    public string? Description { get; private set; }
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
    // ── What was sold ─────────────────────────────────────────────────────────────────────────
    // Description (above) says WHICH SALE this is; these say WHAT product. Both are needed to audit a
    // commission: "Contrato Acme 2026" plus "Industrial Press 3000 / SKU MCH-0042".
    //
    // ProductName is for humans. ProductSku is the discrete, comparable one — it is what rule triggers
    // will filter on once they can, which is why it is kept as its own field instead of being parsed
    // out of a label. Both are DESCRIPTIVE today: nothing reads them for calculation, attribution or
    // idempotency yet. Null wherever the origin supplies nothing (deal-level rows, older data, an
    // uncatalogued line item).
    public string? ProductName { get; private set; }
    public string? ProductSku { get; private set; }
    // Enrichment output (WI-ENRICHMENT): a stable, discrete category resolved at ingest from a
    // tenant-maintained lookup table (ProductSku first, ProductName as fallback). Rule triggers filter
    // on THIS instead of on the raw origin field, so the discriminating value being in the "wrong"
    // column (LAP-12 in ProductName) no longer means a rule silently never fires. Null when no mapping
    // matched — the transaction still processes normally for rules that don't filter on category.
    public string? Category { get; private set; }
    public string? ExternalId { get; private set; }
    // The admin's EXPLICIT attribution decision, captured at manual ingest when the payee belongs to
    // more than one applicable plan. It is a declaration of intent, NOT a computed result: the engine
    // must credit THIS assignment instead of applying its tie-break. Null for every other origin
    // (Excel, HubSpot, pre-existing rows) and for payees with a single unambiguous plan — those keep
    // resolving exactly as before. Cleared whenever the payee changes, since the decision was made
    // about a different person's plans and cannot meaningfully survive that.
    public Guid? SelectedPlanAssignmentId { get; private set; }
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
        int quantity = 1,
        string? description = null,
        Guid? selectedPlanAssignmentId = null,
        string? productName = null,
        string? productSku = null,
        string? category = null)
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
        if (selectedPlanAssignmentId.HasValue && selectedPlanAssignmentId.Value == Guid.Empty)
            throw new DomainException("SelectedPlanAssignmentId must not be empty when provided.");
        if (selectedPlanAssignmentId.HasValue && !payeeId.HasValue)
            throw new DomainException("A plan assignment cannot be selected for a transaction with no payee.");

        var tx = new CompensationTransaction
        {
            Id = id,
            TenantId = tenantId,
            ReferenceNumber = referenceNumber,
            PayeeId = payeeId,
            Amount = amount,
            Quantity = quantity,
            Description = NormalizeDescription(description),
            // Same normalization as Description: trimmed, blank → null, truncated rather than rejected.
            // A product label must never be the reason a real sale fails to ingest.
            ProductName = NormalizeDescription(productName),
            ProductSku = NormalizeDescription(productSku),
            // Same normalization as the other descriptive fields: trimmed, blank → null, truncated
            // rather than rejected. Enrichment must never be the reason a real sale fails to ingest.
            Category = NormalizeDescription(category),
            TransactionDate = transactionDate,
            Source = source,
            Status = CompensationTransactionStatus.Pending,
            ExternalId = externalId,
            SelectedPlanAssignmentId = selectedPlanAssignmentId,
            IngestedAt = now,
            IngestedBy = ingestedBy,
            UpdatedAt = now
        };

        tx.RaiseDomainEvent(new TransactionIngestedEvent(
            eventId, now, tx.Id, tenantId, referenceNumber));

        return tx;
    }

    // Blank → null; trimmed; truncated rather than rejected. A label that is too long must never
    // block ingesting a real sale — this field is descriptive only and carries no money semantics.
    // Public so the import/update preview can show exactly the value that will be stored instead of
    // re-implementing the rule (the two drifting apart is how a preview starts lying).
    public static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var trimmed = description.Trim();
        return trimmed.Length > MaxDescriptionLength
            ? trimmed[..MaxDescriptionLength]
            : trimmed;
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

    // Phase 3: propagate Paid status from the payout that consumed this transaction's credits.
    public void MarkPaid(string updatedBy, DateTimeOffset now, Guid eventId)
    {
        if (Status != CompensationTransactionStatus.Calculated)
            throw new DomainException($"Only Calculated transactions can be marked Paid. Current status: {Status}.");

        Status = CompensationTransactionStatus.Paid;
        UpdatedAt = now;

        RaiseDomainEvent(new TransactionMarkedPaidEvent(eventId, now, Id, TenantId, PayeeId ?? Guid.Empty));
    }

    // Calculated → Pending: called when existing Credits are superseded so the engine can
    // regenerate them with updated plan configuration (e.g. splitAtQuota toggled).
    // Blocked from Paid (anti-double-pay) and Cancelled (terminal) — caller must check.
    public void RevertCalculatedToPending(string updatedBy, DateTimeOffset now)
    {
        if (Status == CompensationTransactionStatus.Paid)
            throw new DomainException("Cannot revert a Paid transaction to Pending — it has already been paid out. Use the accounting correction workflow.");
        if (Status == CompensationTransactionStatus.Cancelled)
            throw new DomainException("Cannot revert a Cancelled transaction to Pending.");
        if (Status != CompensationTransactionStatus.Calculated)
            throw new DomainException($"Only Calculated transactions can be reverted to Pending for recalculation. Current status: {Status}.");

        Status = CompensationTransactionStatus.Pending;
        UpdatedAt = now;
    }

    // Undo Paid status when a payout is reverted. Returns the transaction to Calculated so it
    // can be included in a future payout recalculation.
    public void RevertPaidToCalculated(string updatedBy, DateTimeOffset now)
    {
        if (Status != CompensationTransactionStatus.Paid)
            throw new DomainException($"Only Paid transactions can be reverted to Calculated. Current status: {Status}.");

        Status = CompensationTransactionStatus.Calculated;
        UpdatedAt = now;
    }

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
        // The selection (if any) named an assignment of a DIFFERENT payee — it cannot carry over.
        // Clearing returns this transaction to normal resolution rather than inventing an attribution.
        SelectedPlanAssignmentId = null;
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
        // Same reasoning as Assign: the selected assignment belongs to the previous payee.
        SelectedPlanAssignmentId = null;
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

        if (newPayeeId.HasValue && newPayeeId.Value != PayeeId)
        {
            PayeeId = newPayeeId.Value;
            // Invariant: a plan selection dies with the payee it was made for (see Assign/Reassign).
            SelectedPlanAssignmentId = null;
        }

        // Any value change on a Calculated transaction reverts it to Pending.
        if (Status == CompensationTransactionStatus.Calculated)
            Status = CompensationTransactionStatus.Pending;

        UpdatedAt = now;
    }

    // Description is deliberately NOT part of ApplyExcelUpdate. That method reverts a Calculated
    // transaction to Pending (and its caller supersedes the Credits first) because every field it
    // touches feeds the calculation. Description feeds nothing — it is a label. Routing it through
    // ApplyExcelUpdate would mean that fixing a typo in a deal name invalidates already-calculated
    // commissions, i.e. exactly the "descriptive field must never drive money logic" rule the field
    // was introduced under. So it gets its own method: same Paid guard, no status transition.
    public void UpdateDescription(string? newDescription, string updatedBy, DateTimeOffset now)
    {
        if (Status == CompensationTransactionStatus.Paid)
            throw new DomainException("Cannot update a Paid transaction via Excel re-upload.");

        Description = NormalizeDescription(newDescription);
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
