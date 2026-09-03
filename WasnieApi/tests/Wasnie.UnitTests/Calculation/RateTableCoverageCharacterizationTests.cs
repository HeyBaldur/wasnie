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
/// ★★ THE "NOT ONE CENT" NET FOR KAN-26 TANDA 3, WRITTEN AND RUN AGAINST THE UNTOUCHED ENGINE.
///
/// Tanda 3 changes FOUR branches of the rate step: the attainment bracket lookup, the tiered walk,
/// the split-at-quota walk and the overlap arithmetic inside it. Four branches is four chances to
/// move somebody's pay by accident, so every amount below was read off a run of the engine as it
/// stood before a line was touched — never worked out by hand.
///
/// ★ IF A NUMBER IN THE "MUST NOT MOVE" REGION CHANGES, THE FIX IS WRONG. Do not correct it: a
/// moved amount is a moved payout for somebody.
///
/// ★ THE SECOND REGION IS DIFFERENT AND SAYS SO. Its tests document the four holes exactly as the
/// engine falls into them today. They are the ones — and the only ones — this tanda flips.
///
/// ★★ WHY THIS GOES THROUGH <c>Evaluate</c> AND NOT THROUGH THE PURE MONEY FUNCTIONS.
/// <c>CommissionEngineCharacterizationTests</c> calls <c>ComputeCommission</c> and its siblings
/// directly. Those never see an outcome, a trace or a floor, so that file would stay green whatever
/// this work item does to the branch above them, and a suite that cannot fail is not evidence (§A2).
/// <c>Evaluate</c> is what <c>CreditAllocationService</c> calls for every credit in every pay run.
///
/// ★ THE MALFORMED TABLES ARE VERBATIM FROM PlanRules, and they are built the way PRODUCTION builds
/// them: deserialised by property, never through a factory (§D3/§A4). A factory would refuse most
/// of them, which is exactly why no factory stands between these rows and the engine.
/// </summary>
public sealed class RateTableCoverageCharacterizationTests
{
    private const string EUR = "EUR";
    private static readonly DateOnly TxDate = new(2026, 3, 15);
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions PersistedOptions = BuildPersistedOptions();

    // Mirrors PlanRuleConfiguration.cs:19-25 — the option set that reads persisted rules.
    private static JsonSerializerOptions BuildPersistedOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new MoneyJsonConverter());
        return opts;
    }

    // ── The real rows, verbatim from PlanRules ────────────────────────────────────────────────

    /// "RL-1" (rule EBD3FA15), on an ACTIVE plan. Contiguous, and the last tier stops at 10,000.
    private const string TieredRl1 =
        """{"_schema":1,"type":1,"flatRate":null,"tiers":[{"from":0,"to":1000,"rate":0.05},{"from":1000,"to":5000,"rate":0.08},{"from":5000,"to":10000,"rate":0.09}],"attainmentTiers":null}""";

    /// "Acelerador Laptops" (rule A57BA4C8), on an ACTIVE plan. Stops at 100,000. The only tiered
    /// rule in the database that has ever produced a credit.
    private const string TieredLaptops =
        """{"_schema":1,"type":1,"flatRate":null,"tiers":[{"from":0,"to":10000,"rate":0.04},{"from":10000,"to":25000,"rate":0.06},{"from":25000,"to":100000,"rate":0.08}],"attainmentTiers":null,"splitAtQuota":false}""";

    /// "Comisión Base ARR - Escalonada" (rules C8049D93 / 20322663), archived. Ratios typed into a
    /// table the engine walks over AMOUNTS, with a gap between every pair.
    private const string TieredEnkio =
        """{"_schema":1,"type":1,"flatRate":null,"tiers":[{"from":0,"to":0.7999,"rate":0.075},{"from":0.8,"to":1,"rate":0.1},{"from":1.0001,"to":1.25,"rate":0.125},{"from":1.2501,"to":99.99,"rate":0.15}],"attainmentTiers":null}""";

    /// "Acelerador Hardware Premium" (rules 8A98A3C5 / D0FBE1F4 / 4386E826), all on ACTIVE plans.
    /// Bracket boundaries typed in euros instead of ratios. Malformed — but every ratio in [0, 20000]
    /// still finds tier 1, so this table pays and must keep paying.
    private const string AttAbsolute20k =
        """{"_schema":1,"type":2,"flatRate":null,"tiers":null,"attainmentTiers":[{"attainmentFrom":0,"attainmentTo":20000,"rate":0.04},{"attainmentFrom":20000,"attainmentTo":50000,"rate":0.06},{"attainmentFrom":50000,"attainmentTo":100000,"rate":0.08}],"splitAtQuota":false}""";

    /// "Q1 - (Exaggerated)" (rule A1CDBEA0), archived, 0 assignments. THREE tiers, ALL open, on the
    /// split path — so every euro above the second floor is charged by three tiers at once.
    private const string AttAllOpenSplit =
        """{"_schema":1,"type":2,"flatRate":null,"tiers":null,"attainmentTiers":[{"attainmentFrom":0,"attainmentTo":null,"rate":0.05},{"attainmentFrom":1,"attainmentTo":null,"rate":0.08},{"attainmentFrom":2,"attainmentTo":null,"rate":0.09}],"splitAtQuota":true}""";

    /// A split ladder whose top is CLOSED at quota. Not a row in PlanRules — the shape that reaches
    /// the split walk from a clone or a legacy row, and the one that makes path (c) reproducible.
    private const string AttSplitClosedAtQuota =
        """{"_schema":1,"type":2,"flatRate":null,"tiers":null,"attainmentTiers":[{"attainmentFrom":0,"attainmentTo":1,"rate":0.04}],"splitAtQuota":true}""";

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────

    private static CompensationTransaction Tx(decimal amount) =>
        CompensationTransaction.Ingest(
            tenantId: Guid.NewGuid(),
            referenceNumber: "REF-COV",
            payeeId: Guid.NewGuid(),
            amount: Money.Of(amount, EUR),
            transactionDate: TxDate,
            source: TransactionSource.Manual,
            ingestedBy: "test",
            id: Guid.NewGuid(),
            now: Now,
            eventId: Guid.NewGuid(),
            quantity: 1);

    private static Plan DraftPlan() => Plan.Create(
        tenantId: Guid.NewGuid(), name: "P", description: "d",
        effectivePeriod: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
        currency: EUR, createdBy: "test", id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid());

    /// <summary>
    /// Builds a rule around a table read the way the database hands it over. <c>Plan.AddRule</c>
    /// does not re-validate a ladder — that lives in the factories — which is the same door
    /// <c>Plan.CloneAsNewVersion</c> walks through, and the reason these rows reach the engine at all.
    /// </summary>
    private static Rule RuleFrom(string tableJson, Floor? floor = null)
    {
        var table = JsonSerializer.Deserialize<RateTable>(tableJson, PersistedOptions)!;

        return DraftPlan().AddRule(
            name: "Rule under characterization", sortOrder: 0,
            measurement: new Measurement { Type = MeasurementType.Revenue },
            rateTable: table, floor: floor);
    }

    /// <summary>A ladder built through the REAL factory, to prove today's invariants accept it.</summary>
    private static Rule RuleFromFactory(RateTable table, Floor? floor = null) =>
        DraftPlan().AddRule(
            name: "Rule under characterization", sortOrder: 0,
            measurement: new Measurement { Type = MeasurementType.Revenue },
            rateTable: table, floor: floor);

    private static (decimal Commission, RuleCalculationStep Rate, RuleCalculationStep Floor) Run(
        Rule rule,
        decimal amount,
        decimal attainmentPct = 1.0m,
        AttainmentSource source = AttainmentSource.Measured,
        AttainmentSplitContext? split = null)
    {
        var steps = new List<RuleCalculationStep>();
        var evaluation = CommissionCalculator.Evaluate(
            rule, Tx(amount), EUR, attainmentPct, split, NullLogger.Instance,
            trace: steps, attainmentSource: source);

        return (evaluation.Commission.Amount,
                steps.Single(s => s.Component == RuleCalculationComponent.Rate),
                steps.Single(s => s.Component == RuleCalculationComponent.Floor));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // REGION 1 — MUST NOT MOVE.
    //
    // Every amount here was read off the intact engine. Several of these tables are malformed and
    // stay malformed: the point is that a table the engine CAN price fully keeps paying exactly
    // what it paid, defect and all. Only the edge where the table runs out is this tanda's business.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ THE TWO REAL CREDITS. 81326A30 (29,800 EUR base → 1,684.00) and 744B0D6B (7,450 → 298.00)
    /// are the only credits any tiered rule in this database has ever produced. Both sit under the
    /// 100,000 ceiling, both are unconsumed, and both must come out to the cent.
    /// </summary>
    [Theory]
    [InlineData(29_800, 1684)]
    [InlineData(7_450, 298)]
    public void The_two_real_tiered_credits_are_unmoved(decimal amount, decimal expected)
    {
        var (commission, rate, _) = Run(RuleFrom(TieredLaptops), amount);

        commission.Should().Be(expected);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
    }

    [Theory]
    [InlineData(500, 25)]          // inside tier 1
    [InlineData(1_000, 50)]        // exactly on the first boundary
    [InlineData(7_000, 550)]       // 50 + 320 + 180
    [InlineData(10_000, 820)]      // exactly the top of the ladder — fully priced
    public void A_tiered_amount_the_ladder_covers_pays_what_it_always_paid(decimal amount, decimal expected)
    {
        var (commission, rate, _) = Run(RuleFrom(TieredRl1), amount);

        commission.Should().Be(expected);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
    }

    [Theory]
    [InlineData(0.00, 400)]
    [InlineData(0.50, 400)]
    [InlineData(1.00, 700)]
    [InlineData(3.00, 700)]
    public void A_bracket_the_ladder_contains_pays_what_it_always_paid(decimal attainment, decimal expected)
    {
        var table = RateTable.AttainmentBased(
        [
            new AttainmentTier { AttainmentFrom = 0m, AttainmentTo = 1m, Rate = 0.04m },
            new AttainmentTier { AttainmentFrom = 1m, AttainmentTo = null, Rate = 0.07m },
        ]);

        var (commission, rate, _) = Run(RuleFromFactory(table), 10_000m, attainment);

        commission.Should().Be(expected);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
    }

    /// <summary>
    /// ★★ THE THREE ACTIVE "Acelerador Hardware Premium" RULES. Their brackets are typed in euros,
    /// which is a defect — but a ratio always lands inside [0, 20000], so the lookup always finds
    /// tier 1 and the rule pays 4%. A guard on "no bracket matched" must not touch these: the table
    /// is wrong, and the engine is not failing to price it.
    /// </summary>
    [Theory]
    [InlineData(0.00)]
    [InlineData(0.85)]
    [InlineData(3.00)]
    public void The_absolute_bracket_rules_on_active_plans_keep_paying_their_base_tier(decimal attainment)
    {
        var (commission, rate, _) = Run(RuleFrom(AttAbsolute20k), 10_000m, attainment);

        commission.Should().Be(400m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
    }

    [Theory]
    [InlineData(0, 100_000, 4_000)]        // whole transaction under quota at 4%
    [InlineData(80_000, 40_000, 2_200)]    // 20,000 at 4% + 20,000 at 7%
    [InlineData(150_000, 10_000, 700)]     // entirely above quota at 7%
    public void A_split_walk_a_ladder_covers_pays_what_it_always_paid(
        decimal prior, decimal amount, decimal expected)
    {
        var table = RateTable.AttainmentBased(
        [
            new AttainmentTier { AttainmentFrom = 0m, AttainmentTo = 1m, Rate = 0.04m },
            new AttainmentTier { AttainmentFrom = 1m, AttainmentTo = null, Rate = 0.07m },
        ],
        splitAtQuota: true);

        var (commission, rate, _) = Run(
            RuleFromFactory(table), amount,
            split: new AttainmentSplitContext(prior, 100_000m));

        commission.Should().Be(expected);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
    }

    /// <summary>A floor still lifts a commission the engine really did calculate.</summary>
    [Fact]
    public void A_floor_over_a_priced_amount_still_lifts_it()
    {
        var floor = new Floor { Amount = Money.Of(1_000m, EUR) };

        var (commission, rate, floorStep) = Run(RuleFrom(TieredRl1, floor), 7_000m);

        commission.Should().Be(1_000m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        floorStep.Outcome.Should().Be(RuleCalculationOutcome.Applied);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // REGION 2 — THE FOUR HOLES.
    //
    // ★ EVERY TEST BELOW WAS WRITTEN AGAINST THE INTACT ENGINE AND WENT RED WHEN THE FIX LANDED.
    // Twelve of them, and NOTHING ELSE in 1,876 unit tests — that pairing is the evidence the change
    // hit only what it aimed at. Each one now states the fixed behaviour and keeps the amount the
    // engine used to produce, in a comment, so the "before" stays on the record.
    //
    // ★ THE SHAPE OF THE FIX IS THE SAME EVERY TIME: commission 0, the rate step Skipped, and a
    // RateRefusal code saying which hole it was. The engine does not pay an amount it cannot justify.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// (a) BRACKET LOOKUP WITH NO MATCH. WAS: 0.00 stamped Applied — indistinguishable from a ladder
    /// that deliberately pays nothing at this attainment.
    ///
    /// ★ THIS LADDER IS SAVABLE TODAY, which is why the guard is not merely a legacy-row net. It is
    /// strictly ascending, closed except for the last, touching, and every rate a fraction — all six
    /// shape invariants plus tanda 1's magnitude check pass. Nothing requires a ladder to START at
    /// zero, so every rep under half quota falls off the bottom of it.
    /// </summary>
    [Fact]
    public void Hole_a_an_attainment_ratio_below_the_whole_ladder_is_refused()
    {
        var table = RateTable.AttainmentBased(
        [
            new AttainmentTier { AttainmentFrom = 0.5m, AttainmentTo = 1m, Rate = 0.04m },
            new AttainmentTier { AttainmentFrom = 1m, AttainmentTo = null, Rate = 0.07m },
        ]);

        var (commission, rate, _) = Run(RuleFromFactory(table), 10_000m, attainmentPct: 0.30m);

        commission.Should().Be(0m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        rate.RateRefusal.Should().Be(RateRefusalReason.NoMatchingBracket);
        rate.Operand.Should().Be(0.30m, "the reader has to see WHICH ratio the ladder failed to cover");
    }

    /// <summary>
    /// (b) A TIERED AMOUNT ABOVE THE LAST BOUNDED TIER. WAS: 820.00 stamped Applied for both amounts
    /// below — a 1,000,000 EUR sale paying exactly what a 10,000 EUR sale pays, on an ACTIVE rule.
    /// </summary>
    [Theory]
    [InlineData(10_001)]
    [InlineData(1_000_000)]
    public void Hole_b_a_tiered_amount_over_the_ladder_is_refused(decimal amount)
    {
        var (commission, rate, _) = Run(RuleFrom(TieredRl1), amount);

        commission.Should().Be(0m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        rate.RateRefusal.Should().Be(RateRefusalReason.AmountOutsideTable);
        rate.Tiers.Should().NotBeEmpty("the tiers that DID match stay visible — the reader needs to " +
            "see how far the ladder got before it ran out");
    }

    /// <summary>(b) WAS: 7,300.00 Applied, with 100,000 EUR of revenue dropped without a word.</summary>
    [Fact]
    public void Hole_b_the_active_laptops_rule_refuses_instead_of_evaporating_the_excess()
    {
        var (commission, rate, _) = Run(RuleFrom(TieredLaptops), 200_000m);

        commission.Should().Be(0m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        rate.RateRefusal.Should().Be(RateRefusalReason.AmountOutsideTable);
    }

    /// <summary>
    /// (b) The ENKIO ladder tops out at 99.99, so it priced a 10,000 EUR sale at 14.9222 and called
    /// it Applied. ★ THE HOLE IS NOT ONLY THE BOUNDED TOP: this ladder also has a gap between every
    /// pair of tiers, and the same leftover measure catches both, because it is the walk's own
    /// remainder rather than a second model of the ladder.
    /// </summary>
    [Fact]
    public void Hole_b_a_ratio_shaped_tiered_ladder_is_refused_rather_than_priced_as_pocket_change()
    {
        var (commission, rate, _) = Run(RuleFrom(TieredEnkio), 10_000m);

        commission.Should().Be(0m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        rate.RateRefusal.Should().Be(RateRefusalReason.AmountOutsideTable);
    }

    /// <summary>(c) WAS: 0.00 Applied with an EMPTY tier list — no tier overlapped the transaction.</summary>
    [Fact]
    public void Hole_c_a_split_transaction_above_a_closed_ladder_is_refused()
    {
        var (commission, rate, _) = Run(
            RuleFrom(AttSplitClosedAtQuota), 10_000m,
            split: new AttainmentSplitContext(150_000m, 100_000m));

        commission.Should().Be(0m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        rate.RateRefusal.Should().Be(RateRefusalReason.AmountOutsideTable);
    }

    /// <summary>
    /// (c) The partial form, and the dangerous one. WAS: 200.00 Applied — half the transaction priced
    /// and the other half gone, which does not look like a failure, it looks like a small commission.
    /// </summary>
    [Fact]
    public void Hole_c_a_split_transaction_straddling_the_top_is_refused_not_half_paid()
    {
        var (commission, rate, _) = Run(
            RuleFrom(AttSplitClosedAtQuota), 10_000m,
            split: new AttainmentSplitContext(95_000m, 100_000m));

        commission.Should().Be(0m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        rate.RateRefusal.Should().Be(RateRefusalReason.AmountOutsideTable);
    }

    /// <summary>
    /// (c) THE FIFTH SILENT ZERO, found in Paso 0 and living in the same lines the ticket names for
    /// (c). WAS: 0.00 Applied + Measured. A quota that EXISTS with a target of zero produces a live
    /// split context, and every tier boundary projects onto zero.
    ///
    /// ★ IT REPORTS NoQuotaInEffect, NOT AmountOutsideTable, and the source says NoTarget. There is
    /// no ladder here to be outside OF — this is the same missing target as a null context, and
    /// QuotaAttainmentService.cs:76-79 already calls a zero target exactly that on the bracket path.
    /// One query has to find both.
    /// </summary>
    [Fact]
    public void Hole_c_a_split_context_with_a_zero_target_is_refused_as_a_missing_target()
    {
        var (commission, rate, _) = Run(
            RuleFrom(AttSplitClosedAtQuota), 10_000m,
            split: new AttainmentSplitContext(0m, 0m));

        commission.Should().Be(0m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        rate.RateRefusal.Should().Be(RateRefusalReason.NoQuotaInEffect);
        rate.AttainmentSource.Should().Be(AttainmentSource.NoTarget);
    }

    /// <summary>
    /// (d) OVERLAPPING OPEN TIERS ON THE SPLIT PATH — rule A1CDBEA0, which declares 5%, 8% and 9%
    /// and used to charge all three over the same revenue.
    ///
    /// ★ THIS ONE STILL PAYS, and that is the policy: an overlap is not a hole. Every euro is priced
    /// by exactly one tier — the highest that claims it — so the ladder pays what it declares instead
    /// of a sum of its own rates. WAS: 40,000.00 (a blended 13.33%) and 11,000.00 (a blended 22% on
    /// revenue the table prices at 9%).
    /// </summary>
    [Theory]
    [InlineData(0, 300_000, 22_000)]      // 100k at 5% + 100k at 8% + 100k at 9%
    [InlineData(250_000, 50_000, 4_500)]  // wholly inside the top tier: 9%, the rate the table states
    public void Hole_d_overlapping_open_tiers_are_priced_by_exactly_one_tier(
        decimal prior, decimal amount, decimal expected)
    {
        var (commission, rate, _) = Run(
            RuleFrom(AttAllOpenSplit), amount,
            split: new AttainmentSplitContext(prior, 100_000m));

        commission.Should().Be(expected);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        rate.RateRefusal.Should().BeNull("an overlapping ladder still covers the sale — it was never " +
            "a hole, only a double charge");
    }

    /// <summary>
    /// (d) The tier steps a de-overlapped walk reports. ★ THE PORTIONS MUST SUM TO THE TRANSACTION
    /// AND NO MORE: before the clip they summed to 600,000 on a 300,000 EUR sale, which is the double
    /// charge stated in the trace as plainly as in the amount.
    /// </summary>
    [Fact]
    public void Hole_d_the_walked_portions_sum_to_the_transaction_exactly_once()
    {
        var (_, rate, _) = Run(
            RuleFrom(AttAllOpenSplit), 300_000m,
            split: new AttainmentSplitContext(0m, 100_000m));

        rate.Tiers.Should().HaveCount(3);
        rate.Tiers!.Sum(t => t.Portion).Should().Be(300_000m);
    }

    // ── The floor over a refusal, which is the other half of tanda 2's decision ────────────────

    /// <summary>
    /// WAS: 8,520.00 — the floor lifted a refused bracket straight back to its own amount, so "the
    /// credit is zero" was not true for any rule that carries one.
    ///
    /// ★ THE STEP STILL APPEARS AND SAYS Skipped. Somebody auditing a zero on a rule with an 8,520
    /// EUR floor has to see that the floor was CONSULTED AND DECLINED, not wonder if it was forgotten.
    /// </summary>
    [Fact]
    public void Hole_a_floor_over_a_refused_bracket_does_not_resurrect_it()
    {
        var table = RateTable.AttainmentBased(
        [
            new AttainmentTier { AttainmentFrom = 0.5m, AttainmentTo = 1m, Rate = 0.04m },
            new AttainmentTier { AttainmentFrom = 1m, AttainmentTo = null, Rate = 0.07m },
        ]);
        var floor = new Floor { Amount = Money.Of(8_520m, EUR) };

        var (commission, _, floorStep) = Run(
            RuleFromFactory(table, floor), 10_000m, attainmentPct: 0.30m);

        commission.Should().Be(0m);
        floorStep.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        floorStep.Threshold!.Amount.Should().Be(8_520m);
    }

    /// <summary>WAS: 8,520.00 on a 1,000,000 EUR sale whose excess had just been dropped.</summary>
    [Fact]
    public void Hole_b_floor_over_an_unpriced_amount_does_not_resurrect_it()
    {
        var floor = new Floor { Amount = Money.Of(8_520m, EUR) };

        var (commission, _, floorStep) = Run(RuleFrom(TieredRl1, floor), 1_000_000m);

        commission.Should().Be(0m);
        floorStep.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        floorStep.Threshold!.Amount.Should().Be(8_520m);
    }

    /// <summary>
    /// ★ THE GUARD DID NOT WIDEN. A rule whose ladder DOES price the sale keeps its floor, including
    /// on the tables this tanda is about — being malformed is not being refused.
    /// </summary>
    [Fact]
    public void A_floor_survives_on_a_bounded_ladder_that_still_covers_the_sale()
    {
        var floor = new Floor { Amount = Money.Of(1_000m, EUR) };

        var (commission, rate, floorStep) = Run(RuleFrom(TieredRl1, floor), 9_000m);

        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        commission.Should().Be(1_000m);
        floorStep.Outcome.Should().Be(RuleCalculationOutcome.Applied);
    }
}
