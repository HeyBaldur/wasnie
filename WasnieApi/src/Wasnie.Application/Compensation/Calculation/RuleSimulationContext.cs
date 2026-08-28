using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;

namespace Wasnie.Application.Compensation.Calculation;

/// <summary>
/// Whether a rule can be simulated with the context the caller supplied.
///
/// ★★ ONE DECISION, TWO CALLERS. The rule screen's simulator and the assistant's tool both have to
/// answer "can this be computed, or must I ask for a quota figure first?", and the answer must be the
/// same in both — a chat that produces a number where the screen refuses to is worse than either
/// behaviour on its own, because whichever one the user saw first is the one they will trust.
///
/// ★ AND IT REFUSES RATHER THAN GUESSING, which is the whole point. The engine defaults attainment to
/// 1.0 when nobody supplies it, so "just simulate it" does not fail loudly — it reports the commission
/// of a rep at full quota and presents it as anybody's. That figure looks entirely reasonable, which
/// is exactly what makes it dangerous.
/// </summary>
public static class RuleSimulationContext
{
    public static RuleSimulationBlocker BlockerFor(
        RateTable rateTable,
        decimal? attainmentPct,
        decimal? priorCumulative,
        decimal? quotaTarget)
    {
        if (rateTable.Type != RateTableType.AttainmentBased)
        {
            // Flat and Tiered need nothing beyond the transaction itself: tiered walks the portions
            // of this very amount, so it is fully answerable without any outside context.
            return RuleSimulationBlocker.None;
        }

        if (rateTable.SplitAtQuota)
        {
            return priorCumulative.HasValue && quotaTarget.HasValue
                ? RuleSimulationBlocker.None
                : RuleSimulationBlocker.SplitQuotaContextRequired;
        }

        return attainmentPct.HasValue
            ? RuleSimulationBlocker.None
            : RuleSimulationBlocker.AttainmentContextRequired;
    }
}
