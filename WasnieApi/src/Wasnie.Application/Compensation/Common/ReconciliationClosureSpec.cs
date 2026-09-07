using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Compensation.Reconciliation;

namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// "Has a person already reviewed this anomaly and decided to leave it as it stands?" — as a
/// queryable, so every surface that shows the anomaly can ask the same question in SQL.
///
/// ★★ IT EXISTS BECAUSE A CLOSURE IS ABOUT THE ANOMALY, NOT ABOUT ONE SCREEN. KAN-51 built the
/// closure into the Reconciliation Centre's query only, and the dashboard kept alerting about deals
/// somebody had already closed: two screens disagreeing about the same money, which is precisely the
/// drift the Centre was built not to create. The rule lives here so a third surface cannot forget it.
///
/// ★★ THERE ARE TWO COMPARISONS, AND PICKING THE WRONG ONE IS SILENT. Which applies depends on
/// whether the anomaly is an EVENT with an identity or a CONDITION without one:
///
/// <list type="bullet">
/// <item><b>The fact has an id</b> (a <c>DealLostAlert</c>, a <c>CrmDriftAlert</c>, a
/// <c>Credit</c>) → match <c>c.FactKey == fact.Id</c>. The hourly CRM sync calls
/// <c>Refresh()</c> on every OPEN alert and moves its stamp to "now" without anything having
/// changed, so the stamp is only the last sighting, never the fact. A genuinely new loss is a NEW
/// row with a NEW id and returns on its own.</item>
/// <item><b>The fact has no id</b> (a Pending transaction, a plan with no live rules) → match
/// <c>fact.OccurredAt &lt;= c.FactOccurredAt</c>. Nothing re-stamps these on a schedule, so "no
/// newer than what was reviewed" still means what it says: a plan edited after being closed
/// legitimately asks for a fresh look.</item>
/// </list>
///
/// ★★ THE STAMP RULE ON A FACT THAT HAS AN ID IS THE BUG THIS FILE HAS ALREADY SHIPPED TWICE. This
/// doc used to call the stamp comparison "the whole semantics"; the Centre grew the FactKey branch
/// and this one did not, so the dashboard expired every deal-lost closure on the next sync and the
/// two screens disagreed about the same money again — the exact failure the paragraph above says
/// this spec exists to prevent. Four real closures were void before anybody noticed. A new surface
/// MUST answer "does this fact have an id?" before choosing, and
/// <see cref="Wasnie.Application.Compensation.Handlers.Reconciliation.ReconciliationQuery"/>'s seeds
/// are where the answer is declared (<c>FactKey</c>, set explicitly on every seed).
/// </summary>
public static class ReconciliationClosureSpec
{
    /// <summary>
    /// Every closure that could cover an anomaly of this kind and reason.
    ///
    /// ★ THE CALLER STILL CHECKS THE ENTITY AND THE FACT TIME, because those come from the row being
    /// filtered and cannot be captured here. The shape is always the same:
    /// <code>
    /// var closures = ReconciliationClosureSpec.For(db, kind, reason);
    /// // the fact has an id (alert, credit):
    /// ... where !closures.Any(c => c.EntityId == row.EntityId &amp;&amp; c.FactKey == row.Id)
    /// // the fact has none (pending transaction, plan):
    /// ... where !closures.Any(c => c.EntityId == row.Id &amp;&amp; row.OccurredAt &lt;= c.FactOccurredAt)
    /// </code>
    /// Assign it to a local first: EF translates a captured <c>IQueryable</c>, not a method call
    /// sitting inside the predicate.
    /// </summary>
    public static IQueryable<ReconciliationClosure> For(
        IApplicationDbContext db, int entryKind, string reason) =>
        db.ReconciliationClosures.Where(c => c.EntryKind == entryKind && c.Reason == reason);
}
