using FluentAssertions;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.UnitTests.Domain;

public sealed class CreditTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Credit AllocateCredit() => Credit.Allocate(
        tenantId: Guid.NewGuid(),
        transactionId: Guid.NewGuid(),
        payeeId: Guid.NewGuid(),
        planId: Guid.NewGuid(),
        ruleId: Guid.NewGuid(),
        ruleSnapshot: RuleSnapshot.Freeze(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Base Commission",
            RateTable.Flat(0.05m), Trigger.Always(), Now),
        originalAmount: Money.Of(1000m, "EUR"),
        creditedAmount: Money.Of(50m, "EUR"),
        splitPercentage: Percentage.FromPercent(100m),
        role: CreditRole.Primary,
        allocatedBy: "test-user",
        id: Guid.NewGuid(),
        now: Now,
        eventId: Guid.NewGuid());

    // ── WI-CALC-A.0: SupersededAt / SupersededBy default null ────────────────

    [Fact]
    public void Allocate_SupersededAtIsNull()
    {
        var credit = AllocateCredit();
        credit.SupersededAt.Should().BeNull();
    }

    [Fact]
    public void Allocate_SupersededByIsNull()
    {
        var credit = AllocateCredit();
        credit.SupersededBy.Should().BeNull();
    }
}
