using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Integrations.Crm.Drift;

/// <summary>
/// The money-relevant, already-validated values read from a CRM deal that has the SAME key as an existing
/// active transaction. Only Amount (incl. currency) and CloseDate are in scope (owner decision: owner/stage
/// are out of scope for drift). CloseDate is the value to compare against; null deals never count as a date
/// change (the importer only substitutes "today" for missing dates, which must not look like drift).
/// </summary>
public sealed record CrmDriftIncoming(string ExternalDealId, Money Amount, DateOnly? CloseDate);

/// <summary>A deal whose key matched an ACTIVE transaction → candidate for drift evaluation.</summary>
public sealed record CrmDriftCandidate(CrmDriftIncoming Incoming, CompensationTransaction Existing);

/// <summary>What the policy did with one candidate.</summary>
public enum CrmDriftAction
{
    /// <summary>Amount and date both match → idempotent skip (no drift).</summary>
    NoDrift,

    /// <summary>Transaction was Pending → old voided + a new one created with the deal's current values.</summary>
    AutoVoidedAndRecreated,

    /// <summary>Transaction was Calculated/Paid (immutable) → an alert was recorded; nothing was touched.</summary>
    Alerted,

    /// <summary>
    /// Drift on a Pending transaction, but the auto-void could not be performed safely (status changed
    /// out from under us, or the new values were invalid). Degraded to an alert instead of voiding.
    /// </summary>
    AlertedRaceDegraded,
}

public sealed record CrmDriftOutcome(
    string ExternalDealId,
    Guid ExistingTransactionId,
    CrmDriftAction Action,
    Guid? NewTransactionId);

public sealed record CrmDriftResult(IReadOnlyList<CrmDriftOutcome> Outcomes)
{
    public static readonly CrmDriftResult Empty = new(Array.Empty<CrmDriftOutcome>());

    public int NoDriftCount => Outcomes.Count(o => o.Action == CrmDriftAction.NoDrift);
    public int AutoResolvedCount => Outcomes.Count(o => o.Action == CrmDriftAction.AutoVoidedAndRecreated);
    public int AlertedCount =>
        Outcomes.Count(o => o.Action is CrmDriftAction.Alerted or CrmDriftAction.AlertedRaceDegraded);
}

/// <summary>
/// Centralized "a CRM deal changed after import — what now?" policy. THIS is the single place the
/// detection + action rule lives, so the manual import (today) and the future Hangfire polling job (a
/// later WI) both invoke it identically (clean architecture; CRM-neutral). It performs and persists its
/// own work (voids/alerts in one save, then re-created transactions) within the caller's ambient
/// money-critical transaction.
///
/// Anti-double-pay is sacred (Rule 10): auto-void happens ONLY for strictly Pending transactions;
/// Calculated/Paid are never touched, only alerted.
/// </summary>
public interface ICrmDriftPolicy
{
    /// <param name="newTransactionSource">Source stamped on any re-created transaction (e.g. CrmSync).</param>
    /// <param name="crmSourceName">CRM name stored on the alert (e.g. "HubSpot").</param>
    /// <param name="now">Caller clock — kept deterministic across the whole import.</param>
    /// <param name="actor">User id (or "system") performing the reconciliation.</param>
    /// <param name="actorEmail">Email for the audit trail (falls back to actor).</param>
    Task<CrmDriftResult> ReconcileAsync(
        TransactionSource newTransactionSource,
        string crmSourceName,
        IReadOnlyList<CrmDriftCandidate> candidates,
        DateTimeOffset now,
        string actor,
        string actorEmail,
        CancellationToken cancellationToken = default);
}
