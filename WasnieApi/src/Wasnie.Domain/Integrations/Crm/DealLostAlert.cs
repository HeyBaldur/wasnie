using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Integrations.Crm;

/// <summary>
/// A persisted signal that a CRM deal Wasnie already turned into a commission is NO LONGER closed-won —
/// it moved to Lost (or back to an open stage) after the commission was Calculated or Paid. Distinct from
/// <see cref="CrmDriftAlert"/>, which is about a still-won deal whose amount/date changed: a lost deal
/// changed NEITHER, so it cannot be a drift alert (that entity requires a changed field).
///
/// This entity only DETECTS and surfaces. The correction is an explicit admin action:
///   - Calculated → the admin may revert the commission (supersede the credit + Cancel the transaction).
///   - Paid → informational only; clawback of already-paid money is out of scope (handled outside for now).
///
/// Idempotency: at most ONE unresolved alert per (TenantId, Source, TransactionId). Re-detecting the same
/// lost deal refreshes the figures + detection stamp instead of piling up duplicates. Resolving (on revert,
/// or a future dismiss) sets <see cref="ResolvedAt"/> so history is kept and a fresh loss can alert again.
/// </summary>
public sealed class DealLostAlert : Entity
{
    public Guid TenantId { get; private set; }

    /// <summary>CRM the deal came from (e.g. "HubSpot").</summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>The CRM deal id (the deal part of the transaction's ExternalId).</summary>
    public string ExternalDealId { get; private set; } = string.Empty;

    /// <summary>The affected (untouched) Wasnie transaction.</summary>
    public Guid TransactionId { get; private set; }

    /// <summary>Reference number of the affected transaction, kept for display/deep-link (e.g. HUBSPOT-123-456).</summary>
    public string ReferenceNumber { get; private set; } = string.Empty;

    /// <summary>Transaction status at detection (Calculated or Paid) — drives whether a revert action is offered.</summary>
    public CompensationTransactionStatus TransactionStatus { get; private set; }

    // The commission at risk (sum of the transaction's live credited amounts at detection) — shown to the
    // admin so the confirmation modal states exactly how much would be reverted.
    public decimal CommissionAmount { get; private set; }
    public string CommissionCurrency { get; private set; } = string.Empty;

    public DateTimeOffset DetectedAt { get; private set; }
    public string DetectedBy { get; private set; } = string.Empty;

    // Unresolved (ResolvedAt == null) is what the dashboard surfaces. Set on revert or a future dismiss.
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }

    private DealLostAlert() { }

    public static DealLostAlert Create(
        Guid id,
        Guid tenantId,
        string source,
        string externalDealId,
        Guid transactionId,
        string referenceNumber,
        CompensationTransactionStatus transactionStatus,
        decimal commissionAmount,
        string commissionCurrency,
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
        if (string.IsNullOrWhiteSpace(detectedBy))
            throw new DomainException("DetectedBy is required.");

        return new DealLostAlert
        {
            Id = id,
            TenantId = tenantId,
            Source = source.Trim(),
            ExternalDealId = externalDealId.Trim(),
            TransactionId = transactionId,
            ReferenceNumber = referenceNumber,
            TransactionStatus = transactionStatus,
            CommissionAmount = commissionAmount,
            CommissionCurrency = commissionCurrency,
            DetectedAt = detectedAt,
            DetectedBy = detectedBy,
            ResolvedAt = null,
            ResolvedBy = null,
        };
    }

    /// <summary>The same deal is still lost on a later sync — refresh the figures + detection stamp.</summary>
    public void Refresh(
        CompensationTransactionStatus transactionStatus,
        decimal commissionAmount,
        string commissionCurrency,
        DateTimeOffset detectedAt,
        string detectedBy)
    {
        TransactionStatus = transactionStatus;
        CommissionAmount = commissionAmount;
        CommissionCurrency = commissionCurrency;
        DetectedAt = detectedAt;
        DetectedBy = detectedBy;
    }

    /// <summary>Clear the alert without deleting history (on revert of the commission, or a future dismiss).</summary>
    public void Resolve(string resolvedBy, DateTimeOffset now)
    {
        if (ResolvedAt is not null) return;
        ResolvedAt = now;
        ResolvedBy = resolvedBy;
    }
}
