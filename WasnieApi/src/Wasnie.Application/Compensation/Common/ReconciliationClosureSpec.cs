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
/// ★★ THE COMPARISON IS <c>fact &lt;= FactOccurredAt</c>, AND THAT IS THE WHOLE SEMANTICS. A closure
/// covers the fact it reviewed and nothing later: a fresh detection carries a later stamp, falls
/// outside every existing closure, and surfaces again as a new anomaly. Writing <c>==</c> here would
/// make a refreshed alert reappear even when nothing changed; dropping the comparison altogether
/// would hide anomalies that had not happened when the person decided (§B1).
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
    /// ... where !closures.Any(c => c.EntityId == row.Id &amp;&amp; row.OccurredAt &lt;= c.FactOccurredAt)
    /// </code>
    /// Assign it to a local first: EF translates a captured <c>IQueryable</c>, not a method call
    /// sitting inside the predicate.
    /// </summary>
    public static IQueryable<ReconciliationClosure> For(
        IApplicationDbContext db, int entryKind, string reason) =>
        db.ReconciliationClosures.Where(c => c.EntryKind == entryKind && c.Reason == reason);
}
