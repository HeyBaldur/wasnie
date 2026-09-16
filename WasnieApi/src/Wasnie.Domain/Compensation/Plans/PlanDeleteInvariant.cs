namespace Wasnie.Domain.Compensation.Plans;

/// <summary>
/// The ways an attempt to permanently DELETE a plan can be refused, as codes.
///
/// ★ THE SAME CONTRACT <c>RuleStopInvariant</c> CARRIES. These strings are an API: the front end matches
/// them against its own EN/ES/PL translations, and renaming one silently degrades the refusal to its
/// neutral fallback. Add a code and its three translations in the same change.
/// </summary>
public static class PlanDeleteInvariant
{
    /// <summary>
    /// The plan is not Draft. Carries <c>status</c>. An Active or Archived plan has its own lifecycle
    /// (archive) and stays in the database for audit.
    /// </summary>
    public const string NotDraft = "PlanDeleteNotDraft";

    /// <summary>
    /// The Draft has something that points at it — an assignment, a quota, or any money trace. Carries
    /// <c>blockers</c>, the list of <c>PlanDeletionBlocker</c> names found. Deleting it would orphan
    /// those rows, because none of them has a database foreign key to the plan.
    /// </summary>
    public const string HasDependencies = "PlanDeleteHasDependencies";
}
