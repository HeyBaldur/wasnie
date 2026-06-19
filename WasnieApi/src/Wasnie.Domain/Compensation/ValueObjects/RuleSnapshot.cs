using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Rules;

namespace Wasnie.Domain.Compensation.ValueObjects;

public sealed class RuleSnapshot : ValueObject
{
    public Guid RuleId { get; }
    public Guid PlanId { get; }
    public int PlanVersion { get; }
    public string RuleName { get; }
    public RateTable RateTable { get; }
    public Trigger Trigger { get; }
    /// <summary>Measurement config at the time of credit allocation — drives statement explainability.</summary>
    public Measurement Measurement { get; }
    public DateTimeOffset FrozenAt { get; }

    private RuleSnapshot(
        Guid ruleId,
        Guid planId,
        int planVersion,
        string ruleName,
        RateTable rateTable,
        Trigger trigger,
        Measurement measurement,
        DateTimeOffset frozenAt)
    {
        RuleId = ruleId;
        PlanId = planId;
        PlanVersion = planVersion;
        RuleName = ruleName;
        RateTable = rateTable;
        Trigger = trigger;
        Measurement = measurement;
        FrozenAt = frozenAt;
    }

    public static RuleSnapshot Freeze(
        Guid ruleId,
        Guid planId,
        int planVersion,
        string ruleName,
        RateTable rateTable,
        Trigger trigger,
        DateTimeOffset now,
        Measurement? measurement = null) =>
        new(ruleId, planId, planVersion, ruleName, rateTable, trigger,
            measurement ?? new Measurement(), now);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return RuleId;
        yield return PlanVersion;
        yield return FrozenAt;
    }
}
