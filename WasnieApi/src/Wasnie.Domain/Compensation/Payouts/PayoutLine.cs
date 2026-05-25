using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Domain.Compensation.Payouts;

public sealed class PayoutLine : Entity
{
    public Guid PayoutId { get; private set; }
    public Guid CreditId { get; private set; }
    public Guid RuleId { get; private set; }
    public string RuleName { get; private set; } = string.Empty;
    public Money BaseAmount { get; private set; } = null!;
    public Money CommissionAmount { get; private set; } = null!;
    public IReadOnlyList<ModifierApplication> AppliedModifiers { get; private set; } = [];

    private PayoutLine() { }

    internal static PayoutLine Create(
        Guid payoutId,
        Guid creditId,
        Guid ruleId,
        string ruleName,
        Money baseAmount,
        Money commissionAmount,
        IReadOnlyList<ModifierApplication> appliedModifiers) =>
        new()
        {
            Id = Guid.NewGuid(),
            PayoutId = payoutId,
            CreditId = creditId,
            RuleId = ruleId,
            RuleName = ruleName,
            BaseAmount = baseAmount,
            CommissionAmount = commissionAmount,
            AppliedModifiers = appliedModifiers
        };
}
