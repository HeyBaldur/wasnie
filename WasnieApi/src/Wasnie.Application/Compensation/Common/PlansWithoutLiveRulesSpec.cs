using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Compensation.Plans;

namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// "This plan is Active, still ingesting sales, and pays nothing" — every one of its rules has been
/// stopped.
///
/// ★★ EXTRACTED SO TWO SCREENS CANNOT DISAGREE. The dashboard card and the Reconciliation Centre both
/// count this condition, and the ticket's hard requirement is that the same money never shows two
/// different numbers. The predicate now exists once; both call it. It was inlined in
/// <c>GetDashboardSummaryHandler</c> and is unchanged in meaning — only its address moved.
///
/// ★ <c>Rules.Any()</c> IS PART OF THE RULE, NOT NOISE. A plan that never had a rule cannot be
/// Active at all (<c>Plan.Activate</c> demands one), and if a rule ever existed its row is still
/// there. Without this clause a freshly built plan would read as "stopped paying", which it never was.
///
/// ★ <c>IsActive</c> IS THE ENGINE'S OWN PREDICATE — the same one <c>CreditAllocationService</c>
/// filters rules by — so the warning cannot disagree with what the engine actually does.
/// </summary>
public static class PlansWithoutLiveRulesSpec
{
    public static IQueryable<Plan> Queryable(IApplicationDbContext db) =>
        db.CompensationPlans.Where(p =>
            p.Status == PlanStatus.Active
            && p.Rules.Any()
            && !p.Rules.Any(r => r.IsActive));
}
