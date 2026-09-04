namespace Wasnie.Domain.Compensation.Enums;

public enum CompensationPayoutStatus
{
    Calculated = 0,
    Approved = 1,
    Paid = 2,
    Disputed = 3,

    /// <summary>
    /// Closed by a human because it can never be paid: every credit it carries was already paid by a
    /// DIFFERENT payout, so paying this one would pay the same work twice.
    ///
    /// ★★ TERMINAL, AND NOT THE SAME THING AS Disputed. A disputed payout is money somebody is
    /// arguing about and the payee's own ledger summary reports it as such (see
    /// GetPayeeLedgerSummaryHandler). A discarded one is not contested by anyone — the money was
    /// already paid elsewhere, and reporting it as "under dispute" would put a figure nobody is
    /// disputing in front of the person whose pay it describes (§B3: one field, one meaning).
    ///
    /// ★ IT IS A STATUS RATHER THAN A ROW IN A SIDE TABLE because a payout already owns a mutable
    /// lifecycle, and every reader of this enum filters on it explicitly. A parallel "closure" table
    /// would leave the payout sitting in Approved and require each of those readers to learn to
    /// exclude it one by one — which is precisely the defect KAN-51 had to come back and fix.
    /// </summary>
    Discarded = 4
}
