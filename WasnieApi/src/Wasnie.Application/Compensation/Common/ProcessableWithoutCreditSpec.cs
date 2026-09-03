using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Transactions;

namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// "Everything this sale needed in order to pay is in place, and it has no credit."
///
/// ★★ THIS IS THE COMPLEMENT OF <see cref="UnprocessablePendingSpec"/>, AND THAT IS THE WHOLE POINT.
/// That spec names the Pending transactions that CANNOT be processed — no payee, no covering
/// assignment, wrong currency — and the Reconciliation Centre shows every one of them. A Pending
/// transaction that passes all three tests was, by that spec's own definition, PROCESSABLE, so
/// nothing claimed it and nothing showed it. It sat in a state no screen had a name for.
///
/// ★★ IT IS DERIVED STATE, NOT A FLAG (§B5). The obvious alternative was to have the engine stamp
/// the transaction when it finishes with zero credits. That flag would be wrong in both directions:
/// it would be absent from every transaction the engine NEVER RAN OVER — which is precisely how the
/// two rows that produced this ticket got here, see below — and it would linger after a later run
/// paid the transaction, unless something remembered to clear it. The condition is fully knowable
/// from the money, so it is read from the money.
///
/// ★★ IT DELIBERATELY DOES NOT DISTINGUISH "the engine ran and no rule matched" FROM "the engine
/// never ran". Both are the same fact to the person reconciling: a sale that should carry commission
/// carries none. Current state cannot tell the two apart anyway — no run leaves a per-transaction
/// record — and inventing a distinction the data cannot support would be a field with two meanings
/// (§B3). What the screen states is what is true: this one has not been paid and nothing is stopping
/// it.
///
/// ★ HOW THE TWO KNOWN ROWS GOT HERE (KAN-50, measured 2026-09-03). SCC-20260515-0002 and
/// SCC-20260515-0006 were ingested on 2026-06-01, when their payee had no assignment at all; they
/// were correctly NoActiveAssignment. The assignment was created on 2026-06-09, which made them
/// payable — and nothing re-processes a transaction whose assignment appears later.
/// <c>ProcessPendingScope</c> offers no tenant-wide scope, so a run only reaches them if a human
/// triggers one aimed at that payee, plan or assignment, and none ever was. They left the
/// NoActiveAssignment bucket that afternoon and entered no other.
///
/// ★ THE CLAUSES ARE THE ENGINE'S, IN THE ENGINE'S ORDER. Not archived, Active, period covers the
/// date, plan currency equals the transaction currency — one for one with
/// <see cref="PlanAssignmentResolver.Candidates"/>, exactly as <see cref="AmbiguousAttributionSpec"/>
/// does it, and pinned against the in-memory original by
/// <c>ProcessableWithoutCreditAgreesWithCandidatesTests</c>. The same second-expression risk applies
/// here and is answered the same way: the in-memory version is the engine's, and the engine wins.
/// </summary>
public static class ProcessableWithoutCreditSpec
{
    public const string Reason = "ProcessableWithoutCredit";

    /// <summary>
    /// Pending, payable, unpaid.
    ///
    /// ★ THE AMBIGUOUS CASE IS EXCLUDED so the buckets stay mutually exclusive, the way
    /// UnprocessablePendingSpec's three already are. A transaction whose payee has 2+ eligible plans
    /// and no declared choice is <see cref="AmbiguousAttributionSpec"/>'s to report; listing it twice
    /// would make one unpaid sale read as two problems.
    ///
    /// ★ "NO LIVE CREDIT" IS THE PAYMENT TEST, not the Pending status. Status alone would be enough
    /// today — <c>MarkCalculated</c> moves a transaction out of Pending the moment a credit exists —
    /// but the question this row answers is about MONEY, so it is asked of the credits. A superseded
    /// credit does not count: it was replaced and pays nothing.
    /// </summary>
    public static IQueryable<CompensationTransaction> Queryable(IApplicationDbContext db) =>
        db.CompensationTransactions.Where(t =>
            t.Status == CompensationTransactionStatus.Pending
            && t.PayeeId != null
            && !db.Credits.Any(c => c.TransactionId == t.Id && c.SupersededAt == null)
            && db.PlanAssignments.Count(a =>
                a.PayeeId == t.PayeeId
                && a.Status == AssignmentStatus.Active
                && a.EffectivePeriod.Start <= t.TransactionDate
                && a.EffectivePeriod.End >= t.TransactionDate
                && db.CompensationPlans.Any(p =>
                    p.Id == a.PlanId
                    && p.Status != PlanStatus.Archived
                    && p.Currency == t.Amount.Currency)) >= 1
            // Not the ambiguous case: 2+ eligible plans with nothing declared belongs to the other spec.
            && !(t.SelectedPlanAssignmentId == null
                && db.PlanAssignments.Count(a =>
                    a.PayeeId == t.PayeeId
                    && a.Status == AssignmentStatus.Active
                    && a.EffectivePeriod.Start <= t.TransactionDate
                    && a.EffectivePeriod.End >= t.TransactionDate
                    && db.CompensationPlans.Any(p =>
                        p.Id == a.PlanId
                        && p.Status != PlanStatus.Archived
                        && p.Currency == t.Amount.Currency)) >= 2));
}
