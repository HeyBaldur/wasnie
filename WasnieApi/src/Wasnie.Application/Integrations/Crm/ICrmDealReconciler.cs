namespace Wasnie.Application.Integrations.Crm;

/// <summary>
/// Outcome counts of reconciling a batch of CRM deals into Wasnie transactions. Neutral (no user-facing
/// strings) — each caller turns these into its own messaging (the manual import → a result DTO + warnings;
/// the polling job → an audit summary).
/// </summary>
public sealed record CrmSyncResult(
    int DealsRead,
    int Created,
    int AssignedToPayee,
    int Unassigned,
    int SkippedAlreadyImported,
    int SkippedInvalid,
    int NewOwnerMappings,
    int DriftAutoResolved,
    int DriftAlertsRaised,
    int SkippedBlocked,
    int MissingAmount,
    int MissingCurrency,
    int MissingCloseDate,
    int TotalsMismatch)
{
    public static CrmSyncResult Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>True if this run changed anything in Wasnie (created, auto-resolved drift, or new mapping).</summary>
    public bool ChangedAnything => Created > 0 || DriftAutoResolved > 0 || DriftAlertsRaised > 0 || NewOwnerMappings > 0;
}

/// <summary>
/// THE single place that turns a set of CRM deals into Wasnie transactions: idempotent create via the
/// shared <c>ITransactionCreateGuard</c> + change handling via <c>ICrmDriftPolicy</c> + owner→payee
/// resolution via <c>ICrmOwnerResolver</c>. Both the manual "Import deals" command AND the Phase-3 polling
/// job call THIS — the materialisation logic is written once and only invoked, never duplicated.
///
/// READ-ONLY against the CRM (it receives already-fetched deals). Money-relevant: it creates Pending
/// transactions and triggers auto-void of drifted Pending ones — but never touches Calculated/Paid
/// (Rule 10). The caller owns the ambient transaction/audit envelope.
/// </summary>
public interface ICrmDealReconciler
{
    Task<CrmSyncResult> ReconcileAsync(
        Guid tenantId,
        string sourceName,
        IReadOnlyList<CrmDeal> deals,
        IReadOnlyList<CrmOwner> owners,
        string? defaultCurrency,
        string actor,
        string actorEmail,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
