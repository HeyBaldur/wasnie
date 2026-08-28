using Microsoft.Extensions.Logging;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Transactions;

namespace Wasnie.Infrastructure.Compensation.Calculation;

/// <summary>
/// The Application-facing door onto <see cref="CommissionCalculator.Evaluate"/>.
///
/// ★ IT IS DELIBERATELY THIN, AND THAT IS THE POINT. This class computes nothing. It asks the same
/// method the pay run asks, with a list attached so the steps come back. Any arithmetic that
/// appeared here would be a second engine, and the second engine is always the one people read.
/// </summary>
public sealed class RuleCalculationExplainer(ILogger<RuleCalculationExplainer> logger)
    : IRuleCalculationExplainer
{
    public RuleCalculationTrace Explain(
        Rule rule,
        CompensationTransaction transaction,
        string planCurrency,
        decimal? attainmentPct = null,
        AttainmentSplitContext? splitContext = null)
    {
        var steps = new List<RuleCalculationStep>();

        // ★ THE DEFAULT IS STILL 1.0, BECAUSE CHANGING IT WOULD CHANGE PAYOUTS. What changes is that
        // it no longer travels anonymously: when the caller supplied nothing, the rate step is
        // stamped Defaulted, so a reader can tell "this rep is at 140% of quota" from "we assumed
        // 100% because nobody told us".
        var evaluation = CommissionCalculator.Evaluate(
            rule,
            transaction,
            planCurrency,
            attainmentPct ?? 1.0m,
            splitContext,
            logger,
            steps,
            attainmentPct.HasValue ? AttainmentSource.Supplied : AttainmentSource.Defaulted);

        return new RuleCalculationTrace
        {
            CreditGenerated = evaluation.CreditGenerated,
            // Null rather than zero when no credit was generated: "the rule did not apply to you"
            // and "the rule applied and paid you nothing" are different answers.
            Commission = evaluation.CreditGenerated ? evaluation.Commission : null,
            Steps = steps,
        };
    }
}
