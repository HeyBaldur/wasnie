using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// Builds the Total / Paid / Unpaid commission figures for a date range — the three cards, wherever
/// they appear.
///
/// ★★ ONE IMPLEMENTATION FOR THE DASHBOARD AND THE PAYEE PAGE. The payee's figures are the same
/// question narrowed to one person; a second copy would let the two answer differently about the same
/// money, and the only reason these cards are worth anything is that they agree with each other and
/// with the credits list they open.
///
/// ★★ ONE QUERY, PARTITIONED — NEVER THREE. <c>Total = Paid + Unpaid</c> has to hold to the cent, and
/// three independent aggregations over the same table are three chances to drift: one of them acquires
/// a filter the others do not, and the screen starts showing money that does not add up. The credits of
/// the range are read ONCE and that single set is split, so the invariant is a property of the code.
///
/// ★ CLOSED CREDITS ARE IN NONE OF THE THREE. Written off, or settled outside Wasnie — neither paid nor
/// owed. Counting them as Unpaid would claim the company still owes money it decided not to pay;
/// counting them as Paid would call a write-off a payment. They come back separately so the omission is
/// visible rather than money quietly missing (§B1). See <see cref="CreditSettlement"/>.
/// </summary>
public static class CommissionsBandSpec
{
    /// <param name="payeeId">Narrows to one payee. Null covers the whole tenant (the dashboard).</param>
    /// <remarks>
    /// The range is matched on <c>AllocatedAt</c> — the moment the commission came into existence — and
    /// never on the transaction's date. A transaction date can MOVE: a CRM deal's close date changes
    /// after the fact, which is what the drift alerts report. Attributing money by a date that moves
    /// would silently relocate commissions between ranges after they had been read.
    /// </remarks>
    public static async Task<DashboardCommissionsBandDto> BuildAsync(
        IApplicationDbContext db,
        DateOnly from,
        DateOnly to,
        Guid? payeeId,
        CancellationToken ct)
    {
        var fromDto = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDto = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var query = db.Credits
            .Where(c => c.SupersededAt == null)
            .Where(c => c.AllocatedAt >= fromDto && c.AllocatedAt <= toDto);

        if (payeeId is { } id)
        {
            query = query.Where(c => c.PayeeId == id);
        }

        var credits = await query
            .Select(c => new
            {
                c.CreditedAmount.Amount,
                c.CreditedAmount.Currency,
                IsPaid = c.ConsumedAt != null,
                IsClosed = c.ClosedAt != null,
            })
            .ToListAsync(ct);

        var payable = credits.Where(c => !c.IsClosed).ToList();

        static List<CurrencyTotalDto> ByCurrency<T>(
            IEnumerable<T> rows, Func<T, decimal> amount, Func<T, string> currency) =>
            rows.GroupBy(currency)
                .Select(g => new CurrencyTotalDto(g.Sum(amount), g.Key))
                .OrderBy(t => t.Currency)
                .ToList();

        return new DashboardCommissionsBandDto(
            TotalByCurrency: ByCurrency(payable, c => c.Amount, c => c.Currency),
            PaidByCurrency: ByCurrency(payable.Where(c => c.IsPaid), c => c.Amount, c => c.Currency),
            UnpaidByCurrency: ByCurrency(payable.Where(c => !c.IsPaid), c => c.Amount, c => c.Currency),
            ClosedTotalByCurrency: ByCurrency(credits.Where(c => c.IsClosed), c => c.Amount, c => c.Currency));
    }
}
