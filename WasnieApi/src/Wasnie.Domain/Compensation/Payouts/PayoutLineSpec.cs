using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Domain.Compensation.Payouts;

/// <summary>
/// Input data for one payout line, passed to CompensationPayout.Calculate().
/// Keeps PayoutLine.Create() internal while allowing Application layer to supply line data.
/// </summary>
public sealed record PayoutLineSpec(
    Guid CreditId,
    Guid RuleId,
    string RuleName,
    Money BaseAmount,
    Money CommissionAmount,
    IReadOnlyList<ModifierApplication> AppliedModifiers);
