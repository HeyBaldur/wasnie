using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.Infrastructure.Persistence.Serialization;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// KAN-48 — the configuration correction that follows KAN-26 tanda 3.
///
/// ★★ THE ONLY THING A CONFIGURATION FIX HAS TO PROVE IS THAT IT MOVED NOTHING IT DID NOT MEAN TO.
/// Opening the top tier of a ladder is a change to what happens ABOVE the old ceiling. Every amount
/// the ladder already priced has to come out to the cent, or the "fix" quietly repriced live rules.
/// So each ladder is run BEFORE and AFTER through <c>Evaluate</c> — the entry point the pay run
/// calls — and the in-table amounts are asserted to be equal to each other, not to a number typed
/// here from arithmetic done by hand.
///
/// ★ THE "BEFORE" LADDERS ARE VERBATIM FROM PlanRules, deserialised by property the way production
/// reads them. The "after" ladders are built through the REAL factory, which is the second thing
/// this file proves: the corrected shape passes today's invariants, so it can actually be saved.
/// </summary>
public sealed class LadderCorrectionKan48Tests
{
    private static readonly DateOnly TxDate = new(2026, 3, 15);
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions PersistedOptions = BuildPersistedOptions();

    private static JsonSerializerOptions BuildPersistedOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new MoneyJsonConverter());
        return opts;
    }

    // ── BEFORE: the rows as they stand in PlanRules ───────────────────────────────────────────

    /// Rule EBD3FA15 "RL-1", plan B0EBC74A "Pounds 2 Rules" (GBP, Active). Top tier stops at 10,000.
    private const string Rl1Before =
        """{"_schema":1,"type":1,"flatRate":null,"tiers":[{"from":0,"to":1000,"rate":0.05},{"from":1000,"to":5000,"rate":0.08},{"from":5000,"to":10000,"rate":0.09}],"attainmentTiers":null}""";

    /// Rule A57BA4C8 "Acelerador Laptops", plan 12037AE0 "Q3 2026 - Plan HubSpot E2E" (EUR, Active).
    /// Top tier stops at 100,000. The only tiered rule in the database that has produced credits.
    private const string LaptopsBefore =
        """{"_schema":1,"type":1,"flatRate":null,"tiers":[{"from":0,"to":10000,"rate":0.04},{"from":10000,"to":25000,"rate":0.06},{"from":25000,"to":100000,"rate":0.08}],"attainmentTiers":null,"splitAtQuota":false}""";

    // ── AFTER: the same ladders with the top tier opened, built through the real factory ──────
    //
    // ★ ONLY THE LAST TIER'S UPPER BOUND CHANGES. Not a rate, not a boundary, not the number of
    // tiers. The correction is the smallest edit that makes the ladder cover its own subject, which
    // is why "everything below the old ceiling is unchanged" is a claim this file can make at all.

    internal static RateTable Rl1After() => RateTable.Tiered(
    [
        new RateTier { From = 0m,     To = 1000m, Rate = 0.05m },
        new RateTier { From = 1000m,  To = 5000m, Rate = 0.08m },
        new RateTier { From = 5000m,  To = null,  Rate = 0.09m },
    ]);

    internal static RateTable LaptopsAfter() => RateTable.Tiered(
    [
        new RateTier { From = 0m,      To = 10000m, Rate = 0.04m },
        new RateTier { From = 10000m,  To = 25000m, Rate = 0.06m },
        new RateTier { From = 25000m,  To = null,   Rate = 0.08m },
    ]);

    // ── Harness ───────────────────────────────────────────────────────────────────────────────

    private static CompensationTransaction Tx(decimal amount, string currency) =>
        CompensationTransaction.Ingest(
            tenantId: Guid.NewGuid(), referenceNumber: "REF-KAN48", payeeId: Guid.NewGuid(),
            amount: Money.Of(amount, currency), transactionDate: TxDate,
            source: TransactionSource.Manual, ingestedBy: "test",
            id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid(), quantity: 1);

    private static Rule RuleWith(RateTable table, string currency)
    {
        var plan = Plan.Create(
            tenantId: Guid.NewGuid(), name: "P", description: "d",
            effectivePeriod: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            currency: currency, createdBy: "test", id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid());

        return plan.AddRule(
            name: "Rule under correction", sortOrder: 0,
            measurement: new Measurement { Type = MeasurementType.Revenue },
            rateTable: table);
    }

    private static RateTable Persisted(string json) =>
        JsonSerializer.Deserialize<RateTable>(json, PersistedOptions)!;

    private static (decimal Commission, RuleCalculationOutcome Outcome, RateRefusalReason? Refusal) Run(
        RateTable table, decimal amount, string currency)
    {
        var steps = new List<RuleCalculationStep>();
        var evaluation = CommissionCalculator.Evaluate(
            RuleWith(table, currency), Tx(amount, currency), currency,
            attainmentPct: 1.0m, splitContext: null, logger: NullLogger.Instance,
            trace: steps, attainmentSource: AttainmentSource.Measured);

        var rate = steps.Single(s => s.Component == RuleCalculationComponent.Rate);
        return (evaluation.Commission.Amount, rate.Outcome, rate.RateRefusal);
    }

    // ══ The claim the ticket asks for: in-table amounts pay identically ════════════════════════

    /// <summary>
    /// ★★ THE CENTRAL ASSERTION, AND IT COMPARES THE TWO LADDERS TO EACH OTHER. No expected amount
    /// is typed in: the before ladder and the after ladder are both run and required to agree. A
    /// hand-computed figure could be wrong in the same direction as the fix and nobody would know.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(1_000)]      // the first boundary
    [InlineData(4_999.99)]
    [InlineData(5_000)]      // the second boundary
    [InlineData(7_000)]
    [InlineData(9_999.99)]
    [InlineData(10_000)]     // the OLD ceiling, exactly — still fully priced before and after
    public void Rl1_every_amount_the_old_ladder_covered_pays_exactly_the_same(decimal amount)
    {
        var before = Run(Persisted(Rl1Before), amount, "GBP");
        var after = Run(Rl1After(), amount, "GBP");

        before.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        after.Commission.Should().Be(before.Commission);
        after.Outcome.Should().Be(RuleCalculationOutcome.Applied);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7_450)]      // the base of real credit 744B0D6B
    [InlineData(10_000)]
    [InlineData(25_000)]
    [InlineData(29_800)]     // the base of real credit 81326A30
    [InlineData(99_999.99)]
    [InlineData(100_000)]    // the OLD ceiling, exactly
    public void Laptops_every_amount_the_old_ladder_covered_pays_exactly_the_same(decimal amount)
    {
        var before = Run(Persisted(LaptopsBefore), amount, "EUR");
        var after = Run(LaptopsAfter(), amount, "EUR");

        before.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        after.Commission.Should().Be(before.Commission);
        after.Outcome.Should().Be(RuleCalculationOutcome.Applied);
    }

    /// <summary>
    /// ★★ THE TWO REAL CREDITS, BY THEIR STORED AMOUNTS. 81326A30 (base 29,800 → 1,684.00) and
    /// 744B0D6B (base 7,450 → 298.00) are the only credits any tiered rule in this database has
    /// produced, both under the old ceiling and neither consumed. If a recalculation is ever run
    /// against the corrected version, these are the figures it has to reproduce.
    /// </summary>
    [Theory]
    [InlineData(29_800, 1684)]
    [InlineData(7_450, 298)]
    public void The_two_real_credits_recompute_identically_under_the_corrected_ladder(
        decimal baseAmount, decimal storedCredit)
    {
        Run(Persisted(LaptopsBefore), baseAmount, "EUR").Commission.Should().Be(storedCredit);
        Run(LaptopsAfter(), baseAmount, "EUR").Commission.Should().Be(storedCredit);
    }

    // ══ What the correction is FOR: above the old ceiling, refusal becomes payment ═════════════

    /// <summary>
    /// The ticket's own example. Before: the ladder cannot price it and, since KAN-26 tanda 3, the
    /// engine refuses rather than paying 820 for a million. After: the top tier is open, so the
    /// excess earns the top rate the ladder already states.
    /// </summary>
    [Theory]
    [InlineData(10_001, 820.09)]
    [InlineData(1_000_000, 89_920)]
    public void Rl1_above_the_old_ceiling_stops_being_refused_and_pays_the_top_rate(
        decimal amount, decimal expected)
    {
        var before = Run(Persisted(Rl1Before), amount, "GBP");
        before.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        before.Refusal.Should().Be(RateRefusalReason.AmountOutsideTable);

        var after = Run(Rl1After(), amount, "GBP");
        after.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        after.Commission.Should().Be(expected);
    }

    [Theory]
    [InlineData(100_001, 7_300.08)]
    [InlineData(200_000, 15_300)]
    public void Laptops_above_the_old_ceiling_stops_being_refused_and_pays_the_top_rate(
        decimal amount, decimal expected)
    {
        var before = Run(Persisted(LaptopsBefore), amount, "EUR");
        before.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        before.Refusal.Should().Be(RateRefusalReason.AmountOutsideTable);

        var after = Run(LaptopsAfter(), amount, "EUR");
        after.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        after.Commission.Should().Be(expected);
    }

    /// <summary>
    /// ★ THE CORRECTED SHAPE IS SAVABLE, which is not a given: the ladder has to survive the six
    /// shape invariants plus KAN-26 tanda 1's rate magnitude check. Building it through the factory
    /// IS the assertion — <c>Tiered</c> throws otherwise.
    /// </summary>
    [Fact]
    public void Both_corrected_ladders_pass_todays_write_invariants()
    {
        var rl1 = Rl1After();
        var laptops = LaptopsAfter();

        rl1.Tiers![^1].To.Should().BeNull("the last tier must be open — invariant 4");
        laptops.Tiers![^1].To.Should().BeNull();

        // Nothing but the upper bound moved.
        rl1.Tiers.Should().HaveCount(3);
        rl1.Tiers.Select(t => t.Rate).Should().Equal(0.05m, 0.08m, 0.09m);
        rl1.Tiers.Select(t => t.From).Should().Equal(0m, 1000m, 5000m);

        laptops.Tiers.Should().HaveCount(3);
        laptops.Tiers.Select(t => t.Rate).Should().Equal(0.04m, 0.06m, 0.08m);
        laptops.Tiers.Select(t => t.From).Should().Equal(0m, 10000m, 25000m);
    }
}
