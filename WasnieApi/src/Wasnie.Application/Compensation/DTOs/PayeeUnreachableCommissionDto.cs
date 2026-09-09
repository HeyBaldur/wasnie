namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// The commission a payee is owed that no pay run can reach, grouped by the plan it sits on.
/// </summary>
/// <param name="TotalByCurrency">The whole stuck amount, so the screen leads with the number that matters.</param>
/// <param name="Groups">One per plan, each carrying what would have to change to unstick it.</param>
public sealed record PayeeUnreachableCommissionDto(
    IReadOnlyList<CurrencyTotalDto> TotalByCurrency,
    IReadOnlyList<UnreachableCommissionGroupDto> Groups);

/// <param name="Reason">
/// Why this group cannot be paid, as a CODE — see <see cref="UnreachableReason"/>. The words live in
/// the front end's translations, so fixing a wording or adding Polish never needs a redeploy (§C1).
/// </param>
/// <param name="AssignmentId">
/// The assignment that would have to be reactivated. Null when there never was one, which is a
/// different problem and reads differently.
/// </param>
/// <param name="CoversTransactions">
/// The window the underlying transactions actually fall in. ★ IT IS HERE BECAUSE REACTIVATING IS NOT
/// ALWAYS ENOUGH: an assignment whose effective period ended before those transactions still will not
/// pay them, and the reader has to know which pay run period to run afterwards. One real case had five
/// plans payable by a July run and a sixth that needed June.
/// </param>
public sealed record UnreachableCommissionGroupDto(
    Guid PlanId,
    string PlanName,
    string PlanStatus,
    int CreditCount,
    decimal Amount,
    string Currency,
    string Reason,
    Guid? AssignmentId,
    DateOnly? EffectiveStart,
    DateOnly? EffectiveEnd,
    DateOnly? CoversTransactionsFrom,
    DateOnly? CoversTransactionsTo);

/// <summary>
/// Why a group of owed commission has no route to a payout.
///
/// ★ THESE STRINGS ARE AN API. The screen matches them against its own translations; renaming one
/// silently degrades it to a generic sentence that says money is stuck without saying why — which is
/// the exact failure this whole screen exists to end.
/// </summary>
public static class UnreachableReason
{
    /// <summary>
    /// The payee WAS assigned to this plan and is not any more. The commission was earned while the
    /// link was alive; deactivating it left the unpaid part with nowhere to go. Reactivating the
    /// assignment is the fix, and it is a single action.
    /// </summary>
    public const string AssignmentDeactivated = "AssignmentDeactivated";

    /// <summary>
    /// No assignment to this plan exists at all — the credit predates one, or it was deleted. Needs a
    /// new assignment covering the transactions, not a reactivation.
    /// </summary>
    public const string NoAssignment = "NoAssignment";

    /// <summary>
    /// The plan itself is archived. The engine refuses archived plans regardless of the assignment, so
    /// reactivating alone would change nothing here.
    /// </summary>
    public const string PlanArchived = "PlanArchived";
}
