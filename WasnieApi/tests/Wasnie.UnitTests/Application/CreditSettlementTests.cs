using FluentAssertions;
using Wasnie.Application.Compensation.Common;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-62 follow-up — the settlement axis of a credit.
///
/// The credits screen could only say Active/Superseded, so every row read "Active" and "what is still
/// owed?" had no answer on that screen. These pin the vocabulary the screen and the dashboard now
/// share: they are the same question asked twice, and two copies of it would drift.
/// </summary>
public sealed class CreditSettlementTests
{
    private static readonly DateTimeOffset Now =
        new(new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc), TimeSpan.Zero);

    private static Credit NewCredit(decimal amount = 100m)
    {
        var ruleId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        return Credit.Allocate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), planId, ruleId,
            RuleSnapshot.Freeze(ruleId, planId, 1, "Base", RateTable.Flat(0.05m), Trigger.Always(), Now),
            Money.Of(amount * 20m, "EUR"), Money.Of(amount, "EUR"),
            Percentage.FromPercent(5m), CreditRole.Primary,
            "seed", Guid.NewGuid(), Now, Guid.NewGuid());
    }

    [Fact]
    public void AFreshCreditIsUnpaid()
    {
        CreditSettlement.Of(NewCredit()).Should().Be(CreditSettlement.Unpaid);
    }

    [Fact]
    public void AConsumedCreditIsPaid()
    {
        var credit = NewCredit();
        credit.Consume(Guid.NewGuid(), Now, Guid.NewGuid());

        CreditSettlement.Of(credit).Should().Be(CreditSettlement.Paid);
    }

    [Fact]
    public void RevertingThePaymentPutsItBackToUnpaid()
    {
        // The classification is DERIVED on every read, so a reverted payout stops claiming the money
        // reached the payee. A stored flag would still say Paid.
        var credit = NewCredit();
        credit.Consume(Guid.NewGuid(), Now, Guid.NewGuid());
        credit.Unconsume(Guid.NewGuid(), Now);

        CreditSettlement.Of(credit).Should().Be(CreditSettlement.Unpaid);
    }

    [Theory]
    [InlineData(CreditClosureReason.WrittenOff)]
    [InlineData(CreditClosureReason.ExternalSettlement)]
    public void AClosedCreditIsNeitherPaidNorUnpaid(CreditClosureReason reason)
    {
        var credit = NewCredit();
        credit.Close(reason, "closed by test", "seed", Now, Guid.NewGuid());

        CreditSettlement.Of(credit).Should().Be(CreditSettlement.Closed);
    }

    [Fact]
    public void TheThreeStatesArePairwiseExclusive()
    {
        // Every credit lands in exactly one bucket. If that ever stops holding, Total = Paid + Unpaid
        // on the dashboard stops holding with it.
        var fresh = NewCredit();
        var paid = NewCredit();
        paid.Consume(Guid.NewGuid(), Now, Guid.NewGuid());
        var closed = NewCredit();
        closed.Close(CreditClosureReason.WrittenOff, "n", "seed", Now, Guid.NewGuid());

        new[] { CreditSettlement.Of(fresh), CreditSettlement.Of(paid), CreditSettlement.Of(closed) }
            .Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo([CreditSettlement.Unpaid, CreditSettlement.Paid, CreditSettlement.Closed]);
    }

    [Fact]
    public void ACreditCannotBeBothPaidAndClosed()
    {
        // The domain refuses it, which is what lets Of() test ConsumedAt first without ambiguity.
        var credit = NewCredit();
        credit.Consume(Guid.NewGuid(), Now, Guid.NewGuid());

        var close = () => credit.Close(CreditClosureReason.WrittenOff, "n", "seed", Now, Guid.NewGuid());

        close.Should().Throw<Exception>();
    }

    // ── the query filter ──────────────────────────────────────────────────────

    private sealed record Row(DateTimeOffset? ConsumedAt, DateTimeOffset? ClosedAt);

    [Theory]
    [InlineData("Paid", 1)]
    [InlineData("Unpaid", 1)]
    [InlineData("Closed", 1)]
    [InlineData("Payable", 2)]      // paid + unpaid, never the closed one
    [InlineData("All", 3)]
    [InlineData("nonsense", 3)]     // a stale bookmark shows too much, never breaks the screen
    [InlineData(null, 3)]
    public void ApplyNarrowsToTheRequestedState(string? settlement, int expected)
    {
        var credits = new List<Credit>();

        var unpaid = NewCredit();
        var paid = NewCredit();
        paid.Consume(Guid.NewGuid(), Now, Guid.NewGuid());
        var closed = NewCredit();
        closed.Close(CreditClosureReason.WrittenOff, "n", "seed", Now, Guid.NewGuid());
        credits.AddRange([unpaid, paid, closed]);

        CreditSettlement.Apply(credits.AsQueryable(), settlement).Count().Should().Be(expected);
    }

    [Fact]
    public void PayableIsExactlyPaidPlusUnpaid()
    {
        // The dashboard's Total card sums paid + unpaid and excludes closed credits. If this stopped
        // matching, clicking that card would open a list that does not add up to the figure on it.
        var unpaid = NewCredit();
        var paid = NewCredit();
        paid.Consume(Guid.NewGuid(), Now, Guid.NewGuid());
        var closed = NewCredit();
        closed.Close(CreditClosureReason.WrittenOff, "n", "seed", Now, Guid.NewGuid());
        var all = new List<Credit> { unpaid, paid, closed }.AsQueryable();

        var payable = CreditSettlement.Apply(all, CreditSettlement.Payable).Count();
        var paidOnly = CreditSettlement.Apply(all, CreditSettlement.Paid).Count();
        var unpaidOnly = CreditSettlement.Apply(all, CreditSettlement.Unpaid).Count();

        payable.Should().Be(paidOnly + unpaidOnly);
    }
}
