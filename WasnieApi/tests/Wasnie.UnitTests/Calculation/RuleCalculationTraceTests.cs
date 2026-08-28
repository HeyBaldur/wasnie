using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// The engine reports how it reached a number.
///
/// ★★ THE DEFECT THIS CLOSES HAS NOTHING TO DO WITH A SIMULATOR. Until now the engine reassigned one
/// variable three times and threw the intermediates away, so when somebody asked why their
/// commission was 100 and not 72, nobody could answer with data — the reply had to be reconstructed
/// by hand from the plan's configuration. A calculated payment that cannot be explained is not
/// auditable, and for a commission product in Europe that is a compliance gap, not a missing screen.
///
/// ★ EVERY EXPECTATION HERE IS ABOUT THE NARRATION, NOT THE MONEY. The amounts are pinned separately
/// in CommissionEngineCharacterizationTests, which was written and run BEFORE this refactor existed.
/// </summary>
public sealed class RuleCalculationTraceTests
{
    private const string EUR = "EUR";
    private static readonly DateOnly TxDate = new(2026, 3, 15);
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

    private static readonly IRuleCalculationExplainer Explainer =
        new RuleCalculationExplainer(NullLogger<RuleCalculationExplainer>.Instance);

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static CompensationTransaction Tx(decimal amount, int quantity = 1, string currency = EUR)
    {
        var tx = CompensationTransaction.Ingest(
            tenantId: Guid.NewGuid(),
            referenceNumber: "REF-TRACE",
            payeeId: Guid.NewGuid(),
            amount: Money.Of(amount, currency),
            transactionDate: TxDate,
            source: TransactionSource.Manual,
            ingestedBy: "test",
            id: Guid.NewGuid(),
            now: Now,
            eventId: Guid.NewGuid(),
            quantity: quantity);

        return tx;
    }

    private static Rule MakeRule(
        RateTable? table = null,
        Modifier? modifier = null,
        Cap? cap = null,
        Floor? floor = null,
        Trigger? trigger = null,
        MeasurementType measurement = MeasurementType.Revenue)
    {
        var plan = Plan.Create(
            tenantId: Guid.NewGuid(),
            name: "Trace plan",
            description: "Fixture",
            effectivePeriod: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            currency: EUR,
            createdBy: "test",
            id: Guid.NewGuid(),
            now: Now,
            eventId: Guid.NewGuid());

        return plan.AddRule(
            name: "Rule under trace",
            sortOrder: 0,
            measurement: new Measurement { Type = measurement },
            rateTable: table ?? RateTable.Flat(0.05m),
            trigger: trigger ?? Trigger.Always(),
            modifier: modifier,
            cap: cap,
            floor: floor);
    }

    private static Cap CapOf(decimal a, CapScope scope = CapScope.PerTransaction, string ccy = EUR) =>
        new() { Amount = Money.Of(a, ccy), Scope = scope };

    private static Floor FloorOf(decimal a, string ccy = EUR) => new() { Amount = Money.Of(a, ccy) };

    private static Modifier ModOf(decimal f) => new() { Type = ModifierType.Multiplier, Factor = f };

    private static RuleCalculationStep Step(RuleCalculationTrace t, RuleCalculationComponent c) =>
        t.Steps.Single(s => s.Component == c);

    // ══ ★ The rule from the screenshot, narrated ══════════════════════════

    [Fact]
    public void THE_BREAKDOWN_SHOWS_THE_CAP_APPLIED_AND_THEN_THE_FLOOR_WINNING()
    {
        // ★★ THE WHOLE WORK ITEM IN ONE ASSERTION. 5% of 1,200 = 60 → ×1.2 = 72 → the cap does not
        // bite → the floor lifts it to 100. Anybody reconstructing this from the rule's fields would
        // put the floor before the cap, get 72, and teach the reader an order the engine does not
        // use. The steps come back in the order they RAN.
        var rule = MakeRule(RateTable.Flat(0.05m), ModOf(1.2m), CapOf(10_000m), FloorOf(100m));

        var trace = Explainer.Explain(rule, Tx(1200m), EUR);

        trace.CreditGenerated.Should().BeTrue();
        trace.Commission!.Amount.Should().Be(100m);

        trace.Steps.Select(s => s.Component).Should().Equal(
            RuleCalculationComponent.Trigger,
            RuleCalculationComponent.Base,
            RuleCalculationComponent.Rate,
            RuleCalculationComponent.Modifier,
            RuleCalculationComponent.Cap,
            RuleCalculationComponent.Floor);

        Step(trace, RuleCalculationComponent.Base).Output!.Amount.Should().Be(1200m);
        Step(trace, RuleCalculationComponent.Rate).Output!.Amount.Should().Be(60m);

        var modifier = Step(trace, RuleCalculationComponent.Modifier);
        modifier.Operand.Should().Be(1.2m);
        modifier.Input!.Amount.Should().Be(60m);
        modifier.Output!.Amount.Should().Be(72m);

        var cap = Step(trace, RuleCalculationComponent.Cap);
        cap.Outcome.Should().Be(RuleCalculationOutcome.AppliedWithoutEffect);
        cap.Output!.Amount.Should().Be(72m);

        var floor = Step(trace, RuleCalculationComponent.Floor);
        floor.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        floor.Input!.Amount.Should().Be(72m);
        floor.Output!.Amount.Should().Be(100m);
    }

    [Fact]
    public void A_floor_above_the_cap_is_visible_as_the_cap_biting_and_the_floor_undoing_it()
    {
        // The pathological ordering, narrated: the cap really does pull 100 down to 50, and then the
        // floor really does push it back up to 200. A breakdown that hid the middle step would leave
        // the reader unable to see why the rule paid past its own ceiling.
        var rule = MakeRule(RateTable.Flat(0.10m), null, CapOf(50m), FloorOf(200m));

        var trace = Explainer.Explain(rule, Tx(1000m), EUR);

        var cap = Step(trace, RuleCalculationComponent.Cap);
        cap.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        cap.Input!.Amount.Should().Be(100m);
        cap.Output!.Amount.Should().Be(50m);

        var floor = Step(trace, RuleCalculationComponent.Floor);
        floor.Input!.Amount.Should().Be(50m);
        floor.Output!.Amount.Should().Be(200m);
        trace.Commission!.Amount.Should().Be(200m);
    }

    // ══ ★ Absent, ineffective, and refused are three things ═══════════════

    [Fact]
    public void AN_ABSENT_CAP_AND_A_CAP_THAT_DID_NOT_BITE_ARE_DIFFERENT_STEPS()
    {
        // ★ THE AMOUNTS ARE IDENTICAL — that is exactly why this has to be in the narration. "This
        // rule has no ceiling" and "there is a ceiling and you did not reach it" are different
        // answers to the same question, and only the second one is reassuring.
        var none = Explainer.Explain(MakeRule(RateTable.Flat(0.05m)), Tx(1000m), EUR);
        var loose = Explainer.Explain(MakeRule(RateTable.Flat(0.05m), null, CapOf(10_000m)), Tx(1000m), EUR);

        none.Commission!.Amount.Should().Be(loose.Commission!.Amount);

        Step(none, RuleCalculationComponent.Cap).Outcome
            .Should().Be(RuleCalculationOutcome.NotConfigured);
        Step(none, RuleCalculationComponent.Cap).Threshold.Should().BeNull();

        Step(loose, RuleCalculationComponent.Cap).Outcome
            .Should().Be(RuleCalculationOutcome.AppliedWithoutEffect);
        Step(loose, RuleCalculationComponent.Cap).Threshold!.Amount.Should().Be(10_000m);
    }

    [Fact]
    public void A_cap_the_engine_REFUSES_is_not_reported_as_a_cap_that_did_not_bite()
    {
        // ★ A misconfiguration wearing the costume of a working rule. A cap denominated in another
        // currency is silently ignored by the engine; folded into "no effect" it would look like a
        // ceiling that is quietly protecting nobody.
        var foreign = Explainer.Explain(
            MakeRule(RateTable.Flat(0.05m), null, CapOf(10m, ccy: "USD")), Tx(1000m), EUR);

        Step(foreign, RuleCalculationComponent.Cap).Outcome
            .Should().Be(RuleCalculationOutcome.Skipped);
        foreign.Commission!.Amount.Should().Be(50m);
    }

    // ══ ★ No credit is not a credit of zero ═══════════════════════════════

    [Fact]
    public void A_TRIGGER_THAT_DOES_NOT_MATCH_REPORTS_NO_CREDIT_NOT_A_CREDIT_OF_ZERO()
    {
        // ★★ THE TWO ENDINGS THAT LOOK THE SAME AND ARE NOT. Both end at "you were paid nothing"; one
        // means the rule never applied to this deal, the other means it applied and computed
        // nothing. Telling somebody the second when the first is true sends them to argue about a
        // rate when the real answer is that their deal did not qualify.
        var trigger = Trigger.When(
            LogicalOperator.And,
            new List<Condition>
            {
                new()
                {
                    Field = "Amount",
                    Operator = ConditionOperator.GreaterThan,
                    Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "5000" },
                },
            });

        var trace = Explainer.Explain(MakeRule(trigger: trigger), Tx(1000m), EUR);

        trace.CreditGenerated.Should().BeFalse();
        trace.Commission.Should().BeNull("there is no amount, not an amount of zero");

        trace.Steps.Should().ContainSingle();
        trace.Steps[0].Component.Should().Be(RuleCalculationComponent.Trigger);
        trace.Steps[0].Outcome.Should().Be(RuleCalculationOutcome.NotMatched);
    }

    [Fact]
    public void A_matching_rule_that_computes_nothing_DOES_generate_a_credit_of_zero()
    {
        var trace = Explainer.Explain(MakeRule(RateTable.Flat(0.05m)), Tx(0m), EUR);

        trace.CreditGenerated.Should().BeTrue();
        trace.Commission!.Amount.Should().Be(0m);
    }

    // ══ Units ═════════════════════════════════════════════════════════════

    [Fact]
    public void Units_reports_the_quantity_and_the_per_unit_amount()
    {
        var rule = MakeRule(RateTable.Flat(5.00m), measurement: MeasurementType.Units);

        var trace = Explainer.Explain(rule, Tx(78_500m, quantity: 3), EUR);

        var rate = Step(trace, RuleCalculationComponent.Rate);
        rate.Operand.Should().Be(3m, "the quantity is what the rate multiplies");
        rate.Threshold!.Amount.Should().Be(5.00m, "and this is the money per unit");
        rate.Output!.Amount.Should().Be(15m);

        // ★ The transaction's own amount is still the base, and it is NOT what got multiplied. This
        // is the pair of numbers that produced "78,500 ÷ 5 = 15,700 units" when they were confused.
        Step(trace, RuleCalculationComponent.Base).Output!.Amount.Should().Be(78_500m);
    }

    // ══ Tiered ════════════════════════════════════════════════════════════

    [Fact]
    public void Tiered_reports_every_tier_it_walked_and_what_each_one_earned()
    {
        var table = RateTable.Tiered(new List<RateTier>
        {
            new() { From = 0m,      To = 10_000m, Rate = 0.05m },
            new() { From = 10_000m, To = 50_000m, Rate = 0.08m },
            new() { From = 50_000m, To = null,    Rate = 0.10m },
        });

        var trace = Explainer.Explain(MakeRule(table), Tx(75_000m), EUR);

        var rate = Step(trace, RuleCalculationComponent.Rate);
        rate.RateTable.Should().Be(RateTableType.Tiered);
        rate.Tiers.Should().HaveCount(3);
        rate.Tiers!.Select(t => t.Portion).Should().Equal(10_000m, 40_000m, 25_000m);
        rate.Tiers!.Select(t => t.Amount.Amount).Should().Equal(500m, 3_200m, 2_500m);
        rate.Output!.Amount.Should().Be(6_200m);
    }

    [Fact]
    public void A_tiered_rule_that_stops_inside_the_first_tier_reports_only_that_tier()
    {
        var table = RateTable.Tiered(new List<RateTier>
        {
            new() { From = 0m,      To = 10_000m, Rate = 0.05m },
            new() { From = 10_000m, To = null,    Rate = 0.08m },
        });

        var rate = Step(Explainer.Explain(MakeRule(table), Tx(4_000m), EUR), RuleCalculationComponent.Rate);

        rate.Tiers.Should().ContainSingle("the second tier was never reached, so it is not narrated");
        rate.Tiers![0].Amount.Amount.Should().Be(200m);
    }

    // ══ ★★ The attainment default, made visible ═══════════════════════════

    [Fact]
    public void AN_ATTAINMENT_RULE_WITH_NOBODY_SUPPLYING_A_PERCENTAGE_IS_STAMPED_DEFAULTED()
    {
        // ★★ THE TRAP THIS FIELD EXISTS FOR. The engine initialises attainment to 1.0, so a caller
        // that supplies nothing does not get zero or an error — it gets the numbers of a rep at full
        // quota. Those numbers look completely reasonable and are false for almost everybody. The
        // amount is unchanged (changing it would change payouts); what changes is that it can no
        // longer travel anonymously.
        var trace = Explainer.Explain(MakeRule(Attainment()), Tx(10_000m), EUR);

        var rate = Step(trace, RuleCalculationComponent.Rate);
        rate.Operand.Should().Be(1.0m);
        rate.AttainmentSource.Should().Be(AttainmentSource.Defaulted);
        rate.Output!.Amount.Should().Be(800m);
    }

    [Fact]
    public void A_supplied_percentage_is_stamped_Supplied_and_never_passed_off_as_measured()
    {
        var trace = Explainer.Explain(MakeRule(Attainment()), Tx(10_000m), EUR, attainmentPct: 0.40m);

        var rate = Step(trace, RuleCalculationComponent.Rate);
        rate.Operand.Should().Be(0.40m);
        rate.AttainmentSource.Should().Be(AttainmentSource.Supplied);
        rate.Output!.Amount.Should().Be(200m);
    }

    [Fact]
    public void A_rule_that_does_not_use_attainment_carries_no_source_at_all()
    {
        // Otherwise every flat rule would report "Defaulted" and the flag would mean nothing.
        Step(Explainer.Explain(MakeRule(RateTable.Flat(0.05m)), Tx(1000m), EUR),
                RuleCalculationComponent.Rate)
            .AttainmentSource.Should().BeNull();
    }

    private static RateTable Attainment() => new()
    {
        Type = RateTableType.AttainmentBased,
        AttainmentTiers = new List<AttainmentTier>
        {
            new() { AttainmentFrom = 0m,    AttainmentTo = 1.00m, Rate = 0.02m },
            new() { AttainmentFrom = 1.00m, AttainmentTo = null,  Rate = 0.08m },
        },
    };

    // ══ ★ The trace costs nothing when nobody asks ════════════════════════

    [Fact]
    public void THE_RESULT_IS_IDENTICAL_WHETHER_OR_NOT_A_TRACE_WAS_REQUESTED()
    {
        // ★ THE GUARANTEE THE PAY RUN DEPENDS ON. Production passes no trace; if observing the
        // engine could change it, every explanation would be a lie about a different calculation.
        var rule = MakeRule(RateTable.Flat(0.05m), ModOf(1.2m), CapOf(10_000m), FloorOf(100m));
        var tx = Tx(1200m);

        var untraced = CommissionCalculator.Evaluate(rule, tx, EUR, 1.0m, null);
        var traced = Explainer.Explain(rule, tx, EUR);

        traced.Commission!.Amount.Should().Be(untraced.Commission.Amount);
        traced.CreditGenerated.Should().Be(untraced.CreditGenerated);
    }
}
