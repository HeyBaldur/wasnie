using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Integrations.Crm;

/// <summary>
/// A persisted signal that a CRM deal's money-relevant fields (amount/currency or close date) changed
/// AFTER Wasnie had already turned that deal into a commission that cannot be safely auto-corrected —
/// i.e. the matching transaction is Calculated or Paid.
///
/// Guiding principle (owner decision): HubSpot is the source of truth for the SALE; Wasnie is the source
/// of truth for the PAYMENT. A sale may change freely in the CRM, but once it produced a calculated or
/// paid commission, that record is Wasnie's and is immutable (Rule 10, anti-double-pay). The CRM must
/// never silently overwrite a commission already in flight — so instead of touching anything, we record
/// this alert for an admin to review (surfaced on the dashboard "needs attention" card).
///
/// Pending transactions are auto-reconciled (void the old + re-create with the new values, Opción B) and
/// DO NOT produce an alert — they are fixed automatically. This entity exists only for the dangerous
/// Calculated/Paid cases.
///
/// Idempotency: at most ONE unresolved alert per (TenantId, Source, ExternalDealId, TransactionId). A
/// re-import that still drifts refreshes the new values + detection stamp rather than piling up duplicates.
/// </summary>
public sealed class CrmDriftAlert : Entity
{
    public Guid TenantId { get; private set; }

    /// <summary>CRM the deal came from (e.g. "HubSpot"). Matches <c>CrmOwnerMapping.Source</c>.</summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>The CRM deal id (stored as the transaction's ExternalId).</summary>
    public string ExternalDealId { get; private set; } = string.Empty;

    /// <summary>The affected (untouched) Wasnie transaction.</summary>
    public Guid TransactionId { get; private set; }

    /// <summary>Reference number of the affected transaction, kept for display/deep-link (e.g. HUBSPOT-123).</summary>
    public string ReferenceNumber { get; private set; } = string.Empty;

    /// <summary>Transaction status at detection (Calculated or Paid) — drives the wording shown to the admin.</summary>
    public CompensationTransactionStatus TransactionStatus { get; private set; }

    // ── What changed (old = Wasnie's stored value, immutable; new = the deal's current CRM value) ──
    public bool AmountChanged { get; private set; }
    public decimal OldAmount { get; private set; }
    public string OldCurrency { get; private set; } = string.Empty;
    public decimal NewAmount { get; private set; }
    public string NewCurrency { get; private set; } = string.Empty;

    public bool DateChanged { get; private set; }
    public DateOnly OldCloseDate { get; private set; }
    public DateOnly NewCloseDate { get; private set; }

    public DateTimeOffset DetectedAt { get; private set; }
    public string DetectedBy { get; private set; } = string.Empty;

    // Resolution lets a future "dismiss/acknowledge" action clear an alert without deleting audit history.
    // Unresolved (ResolvedAt == null) is what the dashboard surfaces.
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }

    private CrmDriftAlert() { }

    public static CrmDriftAlert Create(
        Guid id,
        Guid tenantId,
        string source,
        string externalDealId,
        Guid transactionId,
        string referenceNumber,
        CompensationTransactionStatus transactionStatus,
        bool amountChanged,
        decimal oldAmount,
        string oldCurrency,
        decimal newAmount,
        string newCurrency,
        bool dateChanged,
        DateOnly oldCloseDate,
        DateOnly newCloseDate,
        DateTimeOffset detectedAt,
        string detectedBy)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");
        if (string.IsNullOrWhiteSpace(source))
            throw new DomainException("CRM source is required.");
        if (string.IsNullOrWhiteSpace(externalDealId))
            throw new DomainException("External deal id is required.");
        if (transactionId == Guid.Empty)
            throw new DomainException("TransactionId must not be empty.");
        if (string.IsNullOrWhiteSpace(referenceNumber))
            throw new DomainException("Reference number is required.");
        if (!amountChanged && !dateChanged)
            throw new DomainException("A drift alert must record at least one changed field.");
        if (string.IsNullOrWhiteSpace(detectedBy))
            throw new DomainException("DetectedBy is required.");

        return new CrmDriftAlert
        {
            Id = id,
            TenantId = tenantId,
            Source = source.Trim(),
            ExternalDealId = externalDealId.Trim(),
            TransactionId = transactionId,
            ReferenceNumber = referenceNumber,
            TransactionStatus = transactionStatus,
            AmountChanged = amountChanged,
            OldAmount = oldAmount,
            OldCurrency = oldCurrency,
            NewAmount = newAmount,
            NewCurrency = newCurrency,
            DateChanged = dateChanged,
            OldCloseDate = oldCloseDate,
            NewCloseDate = newCloseDate,
            DetectedAt = detectedAt,
            DetectedBy = detectedBy,
            ResolvedAt = null,
            ResolvedBy = null,
        };
    }

    /// <summary>
    /// The deal drifted again (or still drifts). Old values are the transaction's and never change (it is
    /// immutable); refresh the new (CRM) values + detection stamp so the admin always sees the latest figures.
    /// </summary>
    public void Refresh(
        bool amountChanged,
        decimal newAmount,
        string newCurrency,
        bool dateChanged,
        DateOnly newCloseDate,
        DateTimeOffset detectedAt,
        string detectedBy)
    {
        if (!amountChanged && !dateChanged)
            throw new DomainException("A drift alert must record at least one changed field.");

        AmountChanged = amountChanged;
        NewAmount = newAmount;
        NewCurrency = newCurrency;
        DateChanged = dateChanged;
        NewCloseDate = newCloseDate;
        DetectedAt = detectedAt;
        DetectedBy = detectedBy;
    }
}
