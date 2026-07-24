using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;

namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// "This transaction has no resolved category." PURELY INFORMATIONAL — deliberately kept apart from
/// <see cref="UnprocessablePendingSpec"/>.
///
/// An uncategorized transaction is NOT unprocessable: it still credits normally under every rule that
/// does not filter on category (which is most rules today). Folding it into UnprocessablePendingSpec
/// would corrupt that spec's meaning ("cannot be processed") and inflate the dashboard's unprocessable
/// count with rows that process fine. So it lives here, and nothing in the processing job consults it —
/// visibility only, never a gate.
///
/// Scoped to Pending so it reflects the still-open work horizon (the same scope as the other attention
/// signals). Because enrichment is not retroactive (WI decision #2), pre-existing rows stay uncategorized
/// until re-ingested; that is why this is an opt-in list filter, not a prominent always-on count.
/// </summary>
public static class UncategorizedTransactionSpec
{
    public const string Reason = "Uncategorized";

    /// <summary>Pending transactions whose Category was never resolved (no matching lookup at ingest).</summary>
    public static IQueryable<CompensationTransaction> PendingUncategorized(IApplicationDbContext db) =>
        db.CompensationTransactions.Where(t =>
            t.Status == CompensationTransactionStatus.Pending && t.Category == null);
}
