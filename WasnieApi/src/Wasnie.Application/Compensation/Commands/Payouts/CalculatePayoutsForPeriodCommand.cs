using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Payouts;

public sealed record CalculatePayoutsForPeriodCommand(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid? PayeeIdFilter = null) : IRequest<Result<CalculatePayoutsResult>>;

public sealed record CalculatePayoutsResult(
    int PayoutsCreated,
    IReadOnlyList<PayoutConflict> Conflicts,
    IReadOnlyList<PayoutWarning> Warnings,
    /// <summary>Why nothing happened, when nothing happened. See <see cref="PayoutRunDiagnostics"/>.</summary>
    PayoutRunDiagnostics Diagnostics);

/// <summary>
/// What the engine actually did, so a zero can be explained instead of guessed at.
///
/// ★★ THIS EXISTS BECAUSE THE SCREEN WAS INVENTING THE CAUSE. The result used to be a bare
/// <c>PayoutsCreated</c> count, and the UI turned a zero into "No matching credits found for this
/// period" — a sentence the backend never established and which, in the run that prompted this, was
/// false twice over: the engine had skipped four assignments for terminated payees, hit an
/// already-Paid payout on all twenty survivors, and NEVER LOOKED AT A SINGLE CREDIT. An administrator
/// spent three attempts moving date ranges because of that sentence
/// (docs/DIAG_POL-8554_PAYOUT_Y_CREDITOS_INVENTADOS.md).
///
/// ★ CODES, NOT PROSE. <see cref="Skipped"/> carries reason codes and counts; the words live in the
/// front end's translation files. An engine that emitted sentences would need a redeploy to fix a
/// Polish typo, and would have exactly one language.
///
/// ★ AND IT ONLY REPORTS. Not one eligibility rule changed with this type — it makes the existing
/// silence audible, it does not alter who gets paid.
/// </summary>
/// <param name="AssignmentsConsidered">
/// Active assignments whose effective period overlaps the requested one — the population the engine
/// started from, before any discard. Zero means there was nothing to process at all, which is a
/// different answer from "processed things and discarded them" and must read differently.
/// </param>
/// <param name="AssignmentsReachingCreditLookup">
/// How many of those got far enough for the engine to go looking for credits. ★ THE FIELD THAT MAKES
/// THE OLD MESSAGE IMPOSSIBLE: while this is zero, "no matching credits" cannot be said at all,
/// because no credit was ever queried.
/// </param>
/// <param name="CreditsExamined">Credits the engine actually loaded across every assignment it processed.</param>
/// <param name="Skipped">
/// One entry per reason that discarded at least one assignment, by <see cref="PayoutSkipReason"/> code.
/// Reasons that discarded nothing are omitted rather than sent as zeros — a list of zeros is noise, and
/// the reader only needs what happened.
/// </param>
public sealed record PayoutRunDiagnostics(
    int AssignmentsConsidered,
    int AssignmentsReachingCreditLookup,
    int CreditsExamined,
    IReadOnlyList<PayoutSkipCount> Skipped)
{
    /// <summary>A run that never had anything to consider. Used by the engine's earliest exit.</summary>
    public static PayoutRunDiagnostics NothingToConsider => new(0, 0, 0, []);
}

/// <summary>How many assignments one reason discarded. The code is looked up; it is never printed.</summary>
public sealed record PayoutSkipCount(string Code, int Count);

/// <summary>
/// The reason codes, in one place so the engine and the tests cannot drift apart on a spelling.
///
/// ★ THESE STRINGS ARE AN API, NOT A MESSAGE. They are matched by the front end against its own
/// translations; renaming one silently degrades that screen to the neutral fallback, which says a payout
/// was skipped without saying why. Add a code and its EN/ES/PL keys in the same change.
/// </summary>
public static class PayoutSkipReason
{
    /// <summary>
    /// The payee has left. The engine excludes them on purpose — they earn nothing further, and Mark as
    /// paid is irreversible — but until now it did so without telling anybody, which is how a departed
    /// person's commission became invisible on both sides at once.
    /// </summary>
    public const string TerminatedPayee = "TerminatedPayee";

    /// <summary>
    /// The assignment's plan is archived or no longer readable, so no currency could be resolved for it
    /// and the engine cannot produce a payout it could defend.
    /// </summary>
    public const string PlanNotPayable = "PlanNotPayable";

    /// <summary>
    /// An Approved or Paid payout already covers this exact payee, plan and period. The engine refuses
    /// to pay twice; the individual rows are also in <see cref="CalculatePayoutsResult.Conflicts"/>.
    /// </summary>
    public const string ExistingPayout = "ExistingPayout";
}

public sealed record PayoutConflict(
    Guid PayeeId,
    string PayeeName,
    Guid PlanId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status);

public sealed record PayoutWarning(
    Guid PayeeId,
    string PayeeName,
    Guid PlanId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int PendingTransactionCount);
