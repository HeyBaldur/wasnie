using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// Whether a commission has been PAID, is still owed, or left the payable cycle another way.
///
/// ★★ THIS IS A SECOND AXIS, NOT AN EXTENSION OF THE EXISTING STATUS FILTER. The credits screen already
/// filters on Active/Superseded, which answers "was this credit replaced by a reallocation?" — every row
/// reads "Active" because almost none of them were. It says nothing about whether the money reached the
/// payee, so somebody asking "what is still owed?" had to open credits one by one. Folding settlement
/// into that same field would make one column mean two unrelated things (§B3).
///
/// ★ ONE DEFINITION, SHARED. The dashboard's Total/Paid/Unpaid cards and this screen's filter both come
/// from here. They are the same question asked twice, and the whole point of the cards is that clicking
/// one lands on the rows that add up to it — two copies of the predicate would eventually disagree and
/// the screen would contradict the card that opened it.
///
/// "Paid" is <c>ConsumedAt</c> and needs no join to the payout: only the three mark-paid handlers ever
/// set it, and <c>Unconsume</c> clears it when a payment is reverted.
/// </summary>
public static class CreditSettlement
{
    /// <summary>Money reached the payee through a paid payout.</summary>
    public const string Paid = "Paid";

    /// <summary>Still owed and still payable — not paid, not closed.</summary>
    public const string Unpaid = "Unpaid";

    /// <summary>
    /// Left the cycle without a payout: written off, or settled outside Wasnie through payroll.
    /// Neither paid nor owed, which is why it is its own value rather than being folded into either.
    /// </summary>
    public const string Closed = "Closed";

    /// <summary>
    /// Paid + Unpaid: everything still inside the payable cycle, which is exactly what the dashboard's
    /// "Total Commissions" card sums. It exists so that clicking that card opens the rows that add up
    /// to the figure on it — no more, no less.
    /// </summary>
    public const string Payable = "Payable";

    /// <summary>No settlement filter. The screen's default: a filter nobody asked for hides rows.</summary>
    public const string All = "All";

    /// <summary>
    /// Unpaid, and NO pay run can reach it: the payee has no ACTIVE assignment to the credit's plan
    /// covering the credit's period, so <c>CalculatePayoutsForPeriodHandler</c> never even considers it.
    ///
    /// ★★ IT IS A SEPARATE ANSWER BECAUSE "UNPAID" WAS ANSWERING TWO QUESTIONS (§B3). It meant both
    /// "we owe this" and "we owe this and can pay it", and the two are wildly different numbers: one
    /// payee showed €391,736 owed of which €6,005 could actually leave through a pay run. A CFO reading
    /// the first figure is reading a debt sixty-five times the one the system can act on.
    ///
    /// ★ DERIVED ON EVERY READ, NEVER STORED (§B5). Reassigning the payee to the plan makes the same
    /// credit payable again with no migration and no flag to forget; a stored marker would go stale the
    /// moment somebody fixed the assignment.
    /// </summary>
    public const string Unreachable = "Unreachable";

    /// <summary>
    /// The state of one credit, as the code the screen renders. Derived on every read rather than
    /// stored — a stored flag drifts the moment a payout is reverted or a credit is closed.
    /// </summary>
    public static string Of(Credit credit) =>
        credit.ConsumedAt is not null ? Paid
        : credit.ClosedAt is not null ? Closed
        : Unpaid;

    /// <summary>
    /// Narrows a credits query to one settlement state. An unrecognised value degrades to no filter
    /// rather than throwing: a stale bookmark must show too much, never break the screen (§D1).
    /// </summary>
    public static IQueryable<Credit> Apply(IQueryable<Credit> query, string? settlement) =>
        (settlement ?? All).Trim().ToLowerInvariant() switch
        {
            "paid" => query.Where(c => c.ConsumedAt != null),
            "unpaid" => query.Where(c => c.ConsumedAt == null && c.ClosedAt == null),
            "closed" => query.Where(c => c.ClosedAt != null),
            "payable" => query.Where(c => c.ClosedAt == null),
            _ => query,
        };

    /// <summary>
    /// Of a set of unpaid credits, which ones no pay run can reach — because the payee has no ACTIVE
    /// assignment to that plan covering the credit's window.
    ///
    /// ★ MIRRORS THE ENGINE'S OWN GATE. <c>CalculatePayoutsForPeriodHandler</c> starts from
    /// <c>AssignmentStatus.Active</c> assignments whose effective period overlaps the run; a credit
    /// whose plan has no such assignment is never considered, however long it waits. If this drifted
    /// from that gate the screen would promise money the engine will not pay, or hide money it would.
    ///
    /// Returns the ids of the unreachable credits, so the caller can split its own set without a
    /// second trip to the database.
    /// </summary>
    public static async Task<HashSet<Guid>> UnreachableCreditIdsAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<Credit> unpaidCredits,
        CancellationToken ct)
    {
        if (unpaidCredits.Count == 0) return [];

        var payeeIds = unpaidCredits.Select(c => c.PayeeId).Distinct().ToList();
        var planIds = unpaidCredits.Select(c => c.PlanId).Distinct().ToList();

        var activeAssignments = await db.PlanAssignments
            .Where(a => a.Status == AssignmentStatus.Active
                     && payeeIds.Contains(a.PayeeId)
                     && planIds.Contains(a.PlanId))
            .Select(a => new { a.PayeeId, a.PlanId })
            .ToListAsync(ct);

        var reachable = activeAssignments
            .Select(a => (a.PayeeId, a.PlanId))
            .ToHashSet();

        return unpaidCredits
            .Where(c => !reachable.Contains((c.PayeeId, c.PlanId)))
            .Select(c => c.Id)
            .ToHashSet();
    }
}
