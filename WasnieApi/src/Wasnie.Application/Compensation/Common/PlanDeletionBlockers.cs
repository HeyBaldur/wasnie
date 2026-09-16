using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Application.Compensation.Common;

/// <summary>Something that points at a plan and would be orphaned by physically deleting it.</summary>
public enum PlanDeletionBlocker
{
    Assignments,
    Quotas,
    Credits,
    Payouts,
    LedgerEntries,
    ReconciliationClosures,
}

/// <summary>
/// The ONE answer to "does anything point at this plan", shared by the delete handler (which refuses)
/// and the plans list (which hides the Delete action). Two copies would drift, and a list that offers
/// Delete on a plan the handler refuses is a screen that lies (§C3).
///
/// ★★ WHY THE HANDLER HAS TO ASK AND THE DATABASE CANNOT. Only <c>PlanRules</c> has a foreign key to
/// <c>CompensationPlans</c> (cascade). Every table below stores the plan id as a bare GUID, so SQL Server
/// would delete the plan without complaint and leave those rows pointing at nothing — credits and payouts
/// included. This check is the money gate of KAN-69.
///
/// ★ A DRAFT CAN HAVE ALL OF THESE. The screens never offer a Draft for a quota or an assignment, but the
/// API does: <c>AssignPlanToPayeeHandler</c> allows Draft on purpose, <c>QuotaBuilder</c> does not look at
/// status, and <c>CreditAllocationService</c> excludes only Archived plans — so a Draft assigned through
/// the API generates credits. "It is only a Draft" is not evidence that it never touched money.
///
/// ★ ANY STATUS COUNTS. A deactivated assignment, a superseded credit, a discarded payout are still facts
/// with dates (§B6). Tenant query filters are ignored on purpose: the ids come from a plan the caller
/// already loaded under its own tenant, and a row that references it from anywhere is still a reference.
/// </summary>
public static class PlanDeletionBlockers
{
    /// <summary>
    /// For each requested plan id that has at least one dependency, the kinds found. A plan absent from the
    /// result has none. Six bounded queries for the whole set, not six per plan.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlySet<PlanDeletionBlocker>>> FindAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<Guid> planIds,
        CancellationToken cancellationToken)
    {
        var found = new Dictionary<Guid, HashSet<PlanDeletionBlocker>>();
        if (planIds.Count == 0)
            return new Dictionary<Guid, IReadOnlySet<PlanDeletionBlocker>>();

        void Mark(IEnumerable<Guid> ids, PlanDeletionBlocker blocker)
        {
            foreach (var id in ids)
            {
                if (!found.TryGetValue(id, out var set))
                    found[id] = set = [];
                set.Add(blocker);
            }
        }

        Mark(await db.PlanAssignments.IgnoreQueryFilters()
            .Where(a => planIds.Contains(a.PlanId)).Select(a => a.PlanId).Distinct()
            .ToListAsync(cancellationToken), PlanDeletionBlocker.Assignments);

        Mark(await db.Quotas.IgnoreQueryFilters()
            .Where(q => planIds.Contains(q.PlanId)).Select(q => q.PlanId).Distinct()
            .ToListAsync(cancellationToken), PlanDeletionBlocker.Quotas);

        Mark(await db.Credits.IgnoreQueryFilters()
            .Where(c => planIds.Contains(c.PlanId)).Select(c => c.PlanId).Distinct()
            .ToListAsync(cancellationToken), PlanDeletionBlocker.Credits);

        Mark(await db.CompensationPayouts.IgnoreQueryFilters()
            .Where(p => planIds.Contains(p.PlanId)).Select(p => p.PlanId).Distinct()
            .ToListAsync(cancellationToken), PlanDeletionBlocker.Payouts);

        Mark((await db.PayeeLedgerEntries.IgnoreQueryFilters()
            .Where(l => l.SourcePlanId != null && planIds.Contains(l.SourcePlanId.Value))
            .Select(l => l.SourcePlanId!.Value).Distinct()
            .ToListAsync(cancellationToken)), PlanDeletionBlocker.LedgerEntries);

        // A closure's EntityId is a credit, a transaction or a plan; matching on the plan id is enough —
        // ids are GUIDs, so a credit or transaction cannot share one with a plan.
        Mark(await db.ReconciliationClosures.IgnoreQueryFilters()
            .Where(x => planIds.Contains(x.EntityId)).Select(x => x.EntityId).Distinct()
            .ToListAsync(cancellationToken), PlanDeletionBlocker.ReconciliationClosures);

        return found.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<PlanDeletionBlocker>)kv.Value);
    }
}
