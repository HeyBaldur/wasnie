namespace Wasnie.Application.Integrations.Crm;

/// <summary>
/// REVERSE reconciliation (deal-lost detection). The forward path (<see cref="ICrmDealReconciler"/>) only
/// ever sees deals that ARE closed-won, so it is structurally blind to a deal that LEFT closed-won after it
/// was credited. This closes that gap: it takes the deals Wasnie already turned into a Calculated/Paid
/// commission, asks the CRM for their CURRENT won-status by id, and raises a <c>DealLostAlert</c> for any
/// that is no longer won.
///
/// READ + ALERT ONLY. It never touches a credit, a transaction or a payout — the correction is a separate,
/// explicit admin action (revert), never automatic. Absent deals (deleted/archived/no access) are treated
/// conservatively: NOT flagged as lost, to avoid destroying a valid commission on a transient CRM gap.
/// </summary>
public interface IDealLostReconciler
{
    /// <summary>
    /// Checks the tenant's credited CRM deals and raises/refreshes deal-lost alerts. Returns the number of
    /// transactions currently flagged as lost. Persists its own changes.
    /// </summary>
    Task<int> ReconcileAsync(
        Guid tenantId,
        string sourceName,
        string actor,
        string actorEmail,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
