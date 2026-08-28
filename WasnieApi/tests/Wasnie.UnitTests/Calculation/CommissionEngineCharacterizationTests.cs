using FluentAssertions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// ★★ THE "NOT ONE CENT" NET, AND IT WAS WRITTEN BEFORE THE REFACTOR TOUCHED ANYTHING.
///
/// The trace work item is a refactor: the engine has to start reporting HOW it reached a number
/// without changing WHICH number it reaches. The proof of that cannot be "the suite is still green" —
/// a suite stays green by construction if you also edit its expectations. So this file was written
/// first and run against the untouched engine, and every amount below is what the engine ACTUALLY
/// produced then.
///
/// ★ IF A NUMBER IN THIS FILE HAS TO CHANGE, THE REFACTOR IS WRONG. That is the whole contract. Do
/// not "fix" an expectation here: a moved amount is a moved payout for somebody.
///
/// The cascade under test is the real one, in the real order — CreditAllocationService.cs:395-401:
///     rate -> modifier -> cap -> floor
/// which is why the floor can, and here does, win over the cap.
/// </summary>
public sealed class CommissionEngineCharacterizationTests
{
    private const string EUR = "EUR";

    /// <summary>
    /// The production cascade, transcribed from CreditAllocationService.cs:399-401 so the matrix
    /// below exercises the same order the pay run does.
    /// </summary>
    private static Money Cascade(
        Money baseAmount, RateTable table, Modifier? mod, Cap? cap, Floor? floor,
        decimal attainmentPct = 1.0m)
    {
        var c = CommissionCalculator.ComputeCommission(baseAmount, table, attainmentPct);
        c = CommissionCalculator.ApplyModifier(c, baseAmount, mod);
        c = CommissionCalculator.ApplyCap(c, cap);
        c = CommissionCalculator.ApplyFloor(c, floor);
        return c;
    }

    private static Cap CapOf(decimal amount) =>
        new() { Amount = Money.Of(amount, EUR), Scope = CapScope.PerTransaction };

    private static Floor FloorOf(decimal amount) => new() { Amount = Money.Of(amount, EUR) };

    private static Modifier ModOf(decimal factor) =>
        new() { Type = ModifierType.Multiplier, Factor = factor };

    // ══ Flat ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1200, 0.05, 60)]
    [InlineData(0, 0.05, 0)]
    [InlineData(78500, 0.05, 3925)]
    [InlineData(1234.56, 0.075, 92.592)]
    public void Flat_rate_on_an_amount(decimal amount, decimal rate, decimal expected)
    {
        Cascade(Money.Of(amount, EUR), RateTable.Flat(rate), null, null, null)
            .Amount.Should().Be(expected);
    }

    // ══ ★ The rule from the screenshot, and the order that makes it 100 ═══

    [Fact]
    public void THE_FLOOR_WINS_OVER_THE_CAP_BECAUSE_IT_RUNS_AFTER_IT()
    {
        // ★ 5% of 1,200 = 60 -> x1.2 = 72 -> cap 10,000 does not bite -> floor 100 lifts it to 100.
        // Run the same components in "logical" order (floor then cap) and you get 72. The engine's
        // order is the one that pays, and this pins it.
        var result = Cascade(
            Money.Of(1200m, EUR), RateTable.Flat(0.05m),
            ModOf(1.2m), CapOf(10_000m), FloorOf(100m));

        result.Amount.Should().Be(100m);
    }

    [Fact]
    public void A_floor_ABOVE_the_cap_beats_the_cap()
    {
        // The pathological case the ordering creates: the cap says "never more than 50", the floor
        // says "never less than 200", and the floor runs last — so the rule pays 200 despite its
        // own cap.
        Cascade(Money.Of(1000m, EUR), RateTable.Flat(0.10m), null, CapOf(50m), FloorOf(200m))
            .Amount.Should().Be(200m);
    }

    [Fact]
    public void The_cap_bites_when_nothing_lifts_it_afterwards()
    {
        Cascade(Money.Of(1_000_000m, EUR), RateTable.Flat(0.05m), null, CapOf(10_000m), null)
            .Amount.Should().Be(10_000m);
    }

    [Fact]
    public void The_modifier_runs_BEFORE_the_cap_so_it_can_push_a_result_into_it()
    {
        // 5% of 100,000 = 5,000 -> x3 = 15,000 -> capped at 10,000. The modifier running first is
        // what makes the cap bite at all; a modifier after the cap would pay 30,000.
        Cascade(Money.Of(100_000m, EUR), RateTable.Flat(0.05m), ModOf(3m), CapOf(10_000m), null)
            .Amount.Should().Be(10_000m);
    }

    // ══ Tiered ════════════════════════════════════════════════════════════

    [Fact]
    public void Tiered_walks_the_portions_of_the_transaction_itself()
    {
        var table = RateTable.Tiered(new List<RateTier>
        {
            new() { From = 0m,      To = 10_000m, Rate = 0.05m },
            new() { From = 10_000m, To = 50_000m, Rate = 0.08m },
            new() { From = 50_000m, To = null,    Rate = 0.10m },
        });

        // 10,000 @5% = 500 · 40,000 @8% = 3,200 · 25,000 @10% = 2,500
        Cascade(Money.Of(75_000m, EUR), table, null, null, null).Amount.Should().Be(6_200m);
        // Stops inside the first tier.
        Cascade(Money.Of(4_000m, EUR), table, null, null, null).Amount.Should().Be(200m);
        // Lands exactly on a boundary.
        Cascade(Money.Of(10_000m, EUR), table, null, null, null).Amount.Should().Be(500m);
    }

    // ══ Attainment (bracket lookup) ═══════════════════════════════════════

    [Theory]
    [InlineData(0.40, 200)]    // below quota bracket
    [InlineData(1.00, 800)]    // on the boundary — the LAST matching tier wins
    [InlineData(1.50, 800)]
    public void Attainment_picks_the_bracket_for_the_percentage(decimal attainment, decimal expected)
    {
        Cascade(Money.Of(10_000m, EUR), AttainmentTable(), null, null, null, attainment)
            .Amount.Should().Be(expected);
    }

    // ══ ★ The default nobody sees ═════════════════════════════════════════

    [Fact]
    public void AN_ATTAINMENT_RULE_EVALUATED_WITHOUT_CONTEXT_SILENTLY_MEANS_ONE_HUNDRED_PERCENT()
    {
        // ★ THIS IS NOT AN ENDORSEMENT, IT IS A PIN. CreditAllocationService.cs:307 initialises
        // attainmentPct to 1.0m, so a caller that forgets to load quota context does not get zero —
        // it gets the numbers of a rep at full quota, which look entirely reasonable and are false
        // for almost everybody. Pinned here so the refactor cannot quietly alter the default, and so
        // whoever builds on this engine next finds out from a test rather than from a payout.
        Cascade(Money.Of(10_000m, EUR), AttainmentTable(), null, null, null).Amount.Should().Be(800m);
    }

    private static RateTable AttainmentTable() => new()
    {
        Type = RateTableType.AttainmentBased,
        AttainmentTiers = new List<AttainmentTier>
        {
            new() { AttainmentFrom = 0m,    AttainmentTo = 1.00m, Rate = 0.02m },
            new() { AttainmentFrom = 1.00m, AttainmentTo = null,  Rate = 0.08m },
        },
    };

    // ══ Split at quota ════════════════════════════════════════════════════

    [Fact]
    public void Split_at_quota_earns_each_tier_on_the_overlapping_portion()
    {
        var tiers = new List<AttainmentTier>
        {
            new() { AttainmentFrom = 0m,    AttainmentTo = 1.00m, Rate = 0.05m },
            new() { AttainmentFrom = 1.00m, AttainmentTo = null,  Rate = 0.10m },
        };

        // Quota 100,000; already at 80,000; this deal is 40,000 -> 20,000 under @5% + 20,000 over @10%.
        CommissionCalculator
            .ComputeAttainmentSplitCommission(Money.Of(40_000m, EUR), tiers, 80_000m, 100_000m)
            .Amount.Should().Be(3_000m);

        // No quota target at all -> zero, not a crash and not an uncapped rate.
        CommissionCalculator
            .ComputeAttainmentSplitCommission(Money.Of(40_000m, EUR), tiers, 0m, 0m)
            .Amount.Should().Be(0m);
    }

    // ══ Units ═════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1, 5.00, 5)]
    [InlineData(37, 2.50, 92.5)]
    [InlineData(0, 5.00, 0)]
    public void Units_pays_the_rate_per_unit_times_the_quantity(int qty, decimal perUnit, decimal expected)
    {
        CommissionCalculator.ComputeUnitsCommission(qty, perUnit, EUR).Amount.Should().Be(expected);
    }

    // ══ The components that do nothing ════════════════════════════════════

    [Fact]
    public void An_absent_component_and_one_that_does_not_bite_produce_the_SAME_amount()
    {
        // ★ AND THAT IS EXACTLY WHY THE TRACE HAS TO TELL THEM APART. Arithmetically these two rules
        // are indistinguishable; to somebody auditing a payout, "this rule has no cap" and "the cap
        // was checked and the commission never reached it" are different answers.
        var noCap = Cascade(Money.Of(1000m, EUR), RateTable.Flat(0.05m), null, null, null);
        var looseCap = Cascade(Money.Of(1000m, EUR), RateTable.Flat(0.05m), null, CapOf(10_000m), null);

        noCap.Amount.Should().Be(50m);
        looseCap.Amount.Should().Be(50m);
    }

    [Fact]
    public void A_cap_in_another_currency_is_skipped_rather_than_applied()
    {
        var foreignCap = new Cap { Amount = Money.Of(10m, "USD"), Scope = CapScope.PerTransaction };

        Cascade(Money.Of(1000m, EUR), RateTable.Flat(0.05m), null, foreignCap, null)
            .Amount.Should().Be(50m);
    }

    [Fact]
    public void A_PerPeriod_cap_is_ignored_by_this_engine()
    {
        // Such a cap cannot be saved (AddRuleToPlanHandler.cs:30 rejects it), but the engine's own
        // guard is pinned here because the simulator must not invent an answer for it either.
        var periodCap = new Cap { Amount = Money.Of(10m, EUR), Scope = CapScope.PerPeriod };

        Cascade(Money.Of(1000m, EUR), RateTable.Flat(0.05m), null, periodCap, null)
            .Amount.Should().Be(50m);
    }
}
