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
/// ★★ THE "NOT ONE CENT" NET FOR THE NoTarget FIX, WRITTEN AND RUN AGAINST THE UNTOUCHED ENGINE.
///
/// KAN-26 tanda 2 stops an attainment rule paying when nobody set a quota. The danger of that change
/// is not the case it fixes, it is every case it must NOT touch: a rep WITH a quota has to be paid
/// exactly what they were paid before, to the cent. So this file was written first, run against the
/// engine as it stood, and every amount below is what it ACTUALLY produced then — read off that run,
/// never worked out by hand.
///
/// ★ IF A NUMBER IN THIS FILE HAS TO CHANGE, THE FIX IS WRONG. Do not "correct" an expectation here:
/// a moved amount is a moved payout for somebody.
///
/// ★★ WHY A SECOND CHARACTERIZATION FILE INSTEAD OF EXTENDING THE EXISTING ONE.
/// <c>CommissionEngineCharacterizationTests</c> calls <c>ComputeCommission</c> DIRECTLY — the pure
/// money function, which never sees an <c>AttainmentSource</c>. It would stay green whatever this
/// work item does to the branch above it, and a suite that cannot fail is not evidence (§A2). These
/// tests go through <c>Evaluate</c>, the entry point the pay run actually calls
/// (<c>CreditAllocationService</c> passes <c>attainmentSource</c> into it), so the guard is inside
/// the path under test rather than beside it.
/// </summary>
public sealed class NoTargetCharacterizationTests
{
    private const string EUR = "EUR";
    private static readonly DateOnly TxDate = new(2026, 3, 15);
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

    private static CompensationTransaction Tx(decimal amount) =>
        CompensationTransaction.Ingest(
            tenantId: Guid.NewGuid(),
            referenceNumber: "REF-CHAR",
            payeeId: Guid.NewGuid(),
            amount: Money.Of(amount, EUR),
            transactionDate: TxDate,
            source: TransactionSource.Manual,
            ingestedBy: "test",
            id: Guid.NewGuid(),
            now: Now,
            eventId: Guid.NewGuid(),
            quantity: 1);

    /// <summary>
    /// The reference ladder in production: 4% up to quota, 7% above it.
    /// </summary>
    private static Rule AttainmentRule(
        Modifier? modifier = null, Cap? cap = null, Floor? floor = null, bool splitAtQuota = false)
    {
        var plan = Plan.Create(
            tenantId: Guid.NewGuid(), name: "P", description: "d",
            effectivePeriod: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            currency: EUR, createdBy: "test", id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid());

        var table = RateTable.AttainmentBased(
            [
                new AttainmentTier { AttainmentFrom = 0m, AttainmentTo = 1m, Rate = 0.04m },
                new AttainmentTier { AttainmentFrom = 1m, AttainmentTo = null, Rate = 0.07m },
            ],
            splitAtQuota: splitAtQuota);

        return plan.AddRule(
            name: "Attainment rule", sortOrder: 0,
            measurement: new Measurement { Type = MeasurementType.Revenue },
            rateTable: table,
            modifier: modifier, cap: cap, floor: floor);
    }

    private static (decimal Commission, RuleCalculationStep Rate) Run(
        Rule rule,
        decimal amount,
        decimal attainmentPct,
        AttainmentSource source,
        AttainmentSplitContext? split = null)
    {
        var steps = new List<RuleCalculationStep>();
        var evaluation = CommissionCalculator.Evaluate(
            rule, Tx(amount), EUR, attainmentPct, split, NullLogger.Instance,
            trace: steps, attainmentSource: source);

        return (evaluation.Commission.Amount,
                steps.Single(s => s.Component == RuleCalculationComponent.Rate));
    }

    // ══ Measured — the payouts that must not move ═════════════════════════
    //
    // Every expectation below was read off a run of the intact engine.

    [Theory]
    [InlineData(0.00, 400)]     // 0% of a REAL quota: a terrible quarter, still the base bracket
    [InlineData(0.50, 400)]
    [InlineData(0.99, 400)]
    [InlineData(1.00, 700)]     // the shared edge belongs to the upper tier
    [InlineData(1.40, 700)]
    [InlineData(3.00, 700)]
    public void Measured_attainment_pays_exactly_what_it_paid_before(decimal attainment, decimal expected)
    {
        var (commission, rate) = Run(
            AttainmentRule(), amount: 10_000m, attainmentPct: attainment, AttainmentSource.Measured);

        commission.Should().Be(expected);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        rate.AttainmentSource.Should().Be(AttainmentSource.Measured);
    }

    /// <summary>
    /// ★★ THE CASE THE FIX MUST NOT SWALLOW. A rep with a real quota who achieved 0% of it is
    /// Measured, not NoTarget — a fact about a person, not a configuration hole. Identical number,
    /// opposite meaning, and only the source tells them apart. It keeps paying the base bracket.
    /// </summary>
    [Fact]
    public void A_real_zero_per_cent_against_a_real_quota_is_Measured_and_still_pays()
    {
        var (commission, rate) = Run(
            AttainmentRule(), amount: 50_000m, attainmentPct: 0m, AttainmentSource.Measured);

        commission.Should().Be(2000m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        rate.AttainmentSource.Should().Be(AttainmentSource.Measured);
    }

    /// <summary>The whole cascade, so the fix cannot move a modifier, a cap or a floor either.</summary>
    [Fact]
    public void The_full_cascade_on_a_measured_attainment_rule_is_unchanged()
    {
        var rule = AttainmentRule(
            modifier: new Modifier { Type = ModifierType.Multiplier, Factor = 1.5m },
            cap: new Cap { Amount = Money.Of(900m, EUR), Scope = CapScope.PerTransaction },
            floor: new Floor { Amount = Money.Of(100m, EUR) });

        var (commission, _) = Run(rule, amount: 10_000m, attainmentPct: 1.40m, AttainmentSource.Measured);

        // 10,000 x 7% = 700 -> x1.5 = 1,050 -> capped at 900 -> floor 100 does not lift it.
        commission.Should().Be(900m);
    }

    [Fact]
    public void A_supplied_attainment_still_pays_because_the_caller_asserted_it()
    {
        var (commission, rate) = Run(
            AttainmentRule(), amount: 10_000m, attainmentPct: 1.40m, AttainmentSource.Supplied);

        commission.Should().Be(700m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
    }

    // ══ Non-attainment tables are not in this fix's blast radius ══════════

    [Fact]
    public void A_flat_rule_is_untouched_whatever_the_source_says()
    {
        var plan = Plan.Create(
            tenantId: Guid.NewGuid(), name: "P", description: "d",
            effectivePeriod: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            currency: EUR, createdBy: "test", id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid());

        var rule = plan.AddRule(
            name: "Flat", sortOrder: 0,
            measurement: new Measurement { Type = MeasurementType.Revenue },
            rateTable: RateTable.Flat(0.05m));

        var (commission, rate) = Run(rule, amount: 10_000m, attainmentPct: 0m, AttainmentSource.NoTarget);

        commission.Should().Be(500m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        rate.AttainmentSource.Should().BeNull("the source is meaningless on a flat table");
    }

    [Fact]
    public void A_tiered_rule_is_untouched_whatever_the_source_says()
    {
        var plan = Plan.Create(
            tenantId: Guid.NewGuid(), name: "P", description: "d",
            effectivePeriod: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            currency: EUR, createdBy: "test", id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid());

        var rule = plan.AddRule(
            name: "Tiered", sortOrder: 0,
            measurement: new Measurement { Type = MeasurementType.Revenue },
            rateTable: RateTable.Tiered(
            [
                new RateTier { From = 0m, To = 1_000m, Rate = 0.05m },
                new RateTier { From = 1_000m, To = null, Rate = 0.09m },
            ]));

        var (commission, rate) = Run(rule, amount: 10_000m, attainmentPct: 0m, AttainmentSource.NoTarget);

        commission.Should().Be(860m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
    }

    // ══ Split-at-quota already refuses, and must keep refusing the same way ══

    /// <summary>
    /// ★ THE SIBLING BRANCH GOT THIS RIGHT FIRST. Split-at-quota with no quota context already
    /// returns zero with Skipped + NoTarget. Pinned here so the bracket fix lands ON the asymmetry
    /// rather than beside it — and so nobody later "unifies" the two by breaking this one.
    /// </summary>
    [Fact]
    public void Split_at_quota_without_a_quota_already_refuses_to_pay()
    {
        var (commission, rate) = Run(
            AttainmentRule(splitAtQuota: true), amount: 50_000m,
            attainmentPct: 0m, AttainmentSource.NoTarget, split: null);

        commission.Should().Be(0m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        rate.AttainmentSource.Should().Be(AttainmentSource.NoTarget);
    }

    [Fact]
    public void Split_at_quota_with_a_quota_pays_exactly_what_it_paid_before()
    {
        var (commission, rate) = Run(
            AttainmentRule(splitAtQuota: true), amount: 50_000m,
            attainmentPct: 0m, AttainmentSource.Measured,
            split: new AttainmentSplitContext(PriorCumulative: 0m, QuotaTarget: 100_000m));

        commission.Should().Be(2000m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        rate.AttainmentSource.Should().Be(AttainmentSource.Measured);
    }

    // ══ The defect, and what replaced it ═════════════════════════════════

    /// <summary>
    /// ★★ THE FIX, AND THE ONE EXPECTATION IN THIS FILE THAT WAS ALLOWED TO MOVE.
    ///
    /// Written first in its BEFORE form — asserting 2,000 EUR and <c>Applied</c>, which is what the
    /// intact engine produced — and rewritten here only after the run proved that the other thirteen
    /// expectations survived the change untouched. That pairing is the evidence: exactly one test
    /// moved, and it is the one aimed at.
    ///
    /// Two credits worth 7,160 EUR reached a Paid payout down this path. Those are NOT touched by
    /// this work item — the money already paid is a ledger decision (KAN-15). This is forward-only.
    /// </summary>
    [Fact]
    public void A_missing_quota_now_pays_nothing_and_says_so()
    {
        var (commission, rate) = Run(
            AttainmentRule(), amount: 50_000m, attainmentPct: 0m, AttainmentSource.NoTarget);

        commission.Should().Be(0m, "an attainment rule with no quota is incomplete configuration, not a calculation");
        rate.Outcome.Should().Be(
            RuleCalculationOutcome.Skipped,
            "Applied would make it indistinguishable from a legitimate calculation");
        rate.AttainmentSource.Should().Be(AttainmentSource.NoTarget);
        rate.Output!.Amount.Should().Be(0m);
    }

    /// <summary>
    /// ★★ THE RATIO AND ITS SOURCE TRAVEL TOGETHER, AND THIS TEST EXISTS BECAUSE THE FIRST CUT
    /// BROKE THAT. The refusal originally left <c>Operand</c> null, reasoning that a 0 the engine
    /// distrusts should not be published. KAN-27's own suite caught it: the contract is the exact
    /// opposite. The bare ratio is what lies — 0 means both "achieved nothing" and "nothing to
    /// achieve" — and the source beside it is what makes publishing it safe. Dropping the ratio
    /// deletes half the pair and tells the reader less, not more.
    /// </summary>
    [Fact]
    public void The_refusal_still_reports_the_ratio_beside_its_source()
    {
        var (_, rate) = Run(
            AttainmentRule(), amount: 50_000m, attainmentPct: 0m, AttainmentSource.NoTarget);

        rate.Operand.Should().Be(0m);
        rate.AttainmentSource.Should().Be(AttainmentSource.NoTarget);
    }

    /// <summary>
    /// ★★ THE GUARD READS THE SOURCE, NOT THE RATIO — and this is the test that proves it. A
    /// NoTarget reading always carries 0 today, but if it ever carried anything else the rule is
    /// still that there was no target to measure against, so it still must not pay.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1.4)]
    public void No_quota_never_pays_whatever_ratio_travels_with_it(decimal ratio)
    {
        var (commission, rate) = Run(
            AttainmentRule(), amount: 50_000m, attainmentPct: ratio, AttainmentSource.NoTarget);

        commission.Should().Be(0m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
    }

    // ══ Caso 10/11: the floor, after the decision ════════════════════════

    /// <summary>
    /// ★★ CASO 10 — THE FLOOR DOES NOT RESURRECT A REFUSAL. This test used to document the hole:
    /// it asserted 100 EUR, because the floor runs after the rate and lifted the refusal straight
    /// back up. Six real attainment rules carry a floor, two of them 8,520 EUR and 7,800 EUR, so
    /// "the credit is zero" was true everywhere except exactly where it cost the most.
    ///
    /// ★ THE PRODUCT DECISION, AND WHY IT IS NOT ARBITRARY. A floor is the minimum commission on a
    /// COMMISSIONED SALE — a component of the calculation, which presupposes there was one. With no
    /// quota there is no calculation, so there is no commission for a floor to be the minimum of; a
    /// floor paying anyway is an orphan, not a guarantee. The thing that pays regardless of sales is
    /// a DRAW, and a draw lives at rep-and-period level, not on a transaction. Different mechanism,
    /// different ticket.
    /// </summary>
    [Fact]
    public void A_floor_does_not_resurrect_a_commission_the_engine_refused_to_pay()
    {
        var rule = AttainmentRule(floor: new Floor { Amount = Money.Of(8_520m, EUR) });

        var (commission, rate) = Run(rule, amount: 50_000m, attainmentPct: 0m, AttainmentSource.NoTarget);

        commission.Should().Be(0m, "no quota means no calculation, and a floor is part of a calculation");
        rate.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
    }

    /// <summary>
    /// ★ THE FLOOR IS DECLINED OUT LOUD, NOT DROPPED. Somebody auditing a zero on a rule that
    /// carries an 8,520 EUR floor has to be able to see that the floor was consulted and declined,
    /// rather than wonder whether the engine forgot it (§B1). NotConfigured would be a lie — the
    /// rule really does configure one.
    /// </summary>
    [Fact]
    public void The_suppressed_floor_still_appears_in_the_trace_saying_it_was_declined()
    {
        var rule = AttainmentRule(floor: new Floor { Amount = Money.Of(8_520m, EUR) });

        var steps = new List<RuleCalculationStep>();
        CommissionCalculator.Evaluate(
            rule, Tx(50_000m), EUR, 0m, null, NullLogger.Instance,
            trace: steps, attainmentSource: AttainmentSource.NoTarget);

        var floor = steps.Single(x => x.Component == RuleCalculationComponent.Floor);

        floor.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        floor.Threshold!.Amount.Should().Be(8_520m, "the floor that was declined is named, not hidden");
        floor.Output!.Amount.Should().Be(0m);
    }

    /// <summary>
    /// ★★ CASO 11 — AND THIS IS THE HALF THAT MATTERS MOST. Suppressing the floor was only safe if
    /// it is suppressed for NOTHING ELSE. A payee WITH a quota keeps their floor exactly as before.
    /// The amount was read off the intact engine.
    /// </summary>
    [Fact]
    public void With_a_quota_the_floor_applies_exactly_as_it_always_did()
    {
        var rule = AttainmentRule(floor: new Floor { Amount = Money.Of(8_520m, EUR) });

        var (commission, rate) = Run(rule, amount: 50_000m, attainmentPct: 0.5m, AttainmentSource.Measured);

        // 50,000 x 4% = 2,000, lifted to the 8,520 floor.
        commission.Should().Be(8_520m);
        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
    }

    [Fact]
    public void With_a_quota_a_floor_below_the_commission_still_does_not_bite()
    {
        var rule = AttainmentRule(floor: new Floor { Amount = Money.Of(100m, EUR) });

        var (commission, _) = Run(rule, amount: 50_000m, attainmentPct: 1.5m, AttainmentSource.Measured);

        commission.Should().Be(3_500m, "50,000 x 7%, well above the floor");
    }

    /// <summary>
    /// ★ THE SIBLING REFUSAL GETS THE SAME TREATMENT. Split-at-quota with no quota context is the
    /// same NoTarget refusal by a different route, so its floor is suppressed too — otherwise the
    /// asymmetry this work item just closed would reopen one component further down the cascade.
    /// </summary>
    [Fact]
    public void The_split_at_quota_refusal_suppresses_its_floor_too()
    {
        var rule = AttainmentRule(
            floor: new Floor { Amount = Money.Of(8_520m, EUR) }, splitAtQuota: true);

        var (commission, _) = Run(
            rule, amount: 50_000m, attainmentPct: 0m, AttainmentSource.NoTarget, split: null);

        commission.Should().Be(0m);
    }

    /// <summary>
    /// ★ THE SUPPRESSION MUST NOT WIDEN BEYOND ATTAINMENT. A Units rule is not an attainment rule,
    /// so a NoTarget source travelling past it means nothing and its floor keeps working exactly as
    /// before. Pinned because the guard is written as "AttainmentBased AND NoTarget", and dropping
    /// the first half would silently take the floor off every Units spiff in the system.
    ///
    /// ★ WHAT THIS TEST DOES NOT REACH, SAID PLAINLY: the Units MISCONFIGURATION guard (Units paired
    /// with a non-Flat table). The domain refuses that pairing at write time, so it cannot be built
    /// through Plan.AddRule and is only reachable from a stored row. That branch is untested here
    /// and this work item did not change it.
    /// </summary>
    [Fact]
    public void A_units_rule_keeps_its_floor_even_when_a_NoTarget_source_travels_past()
    {
        var plan = Plan.Create(
            tenantId: Guid.NewGuid(), name: "P", description: "d",
            effectivePeriod: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            currency: EUR, createdBy: "test", id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid());

        var rule = plan.AddRule(
            name: "Units spiff", sortOrder: 0,
            measurement: new Measurement { Type = MeasurementType.Units },
            rateTable: RateTable.Flat(2m),
            floor: new Floor { Amount = Money.Of(100m, EUR) });

        var (commission, rate) = Run(rule, amount: 0m, attainmentPct: 0m, AttainmentSource.NoTarget);

        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied, "Units pays per unit, quota or not");
        commission.Should().Be(100m, "2 EUR x 1 unit, lifted by the floor exactly as before");
    }

    // ══ Consultable: the refusal survives into storage ═══════════════════

    /// <summary>
    /// ★★ "MARKED IN A WAY SOMEBODY CAN FIND" IS THE OTHER HALF OF THE POLICY, and it is worth a
    /// test of its own because a refusal nobody can query is a silent zero with better manners.
    ///
    /// The carrier is the trace `CreditAllocationService` already serialises onto every credit
    /// (KAN-27). A NoTarget credit is therefore distinguishable from all three of its neighbours:
    /// from "not processed yet" (no credit row exists at all), from a rule whose trigger did not
    /// match (no credit either — `CreditGenerated` is false), and from a real 0% against a real
    /// quota (a credit whose Rate step says Applied + Measured).
    ///
    /// ★ THE FACT IS DERIVED, NOT FLAGGED (§B5). No boolean is stored beside the money that could
    /// drift out of step with it; the reason lives in the same document as the calculation it
    /// explains, and `JSON_VALUE(CalculationTrace, ...)` finds it.
    /// </summary>
    [Fact]
    public void The_refusal_is_findable_in_the_stored_trace()
    {
        var steps = new List<RuleCalculationStep>();
        var evaluation = CommissionCalculator.Evaluate(
            AttainmentRule(), Tx(50_000m), EUR, 0m, null, NullLogger.Instance,
            trace: steps, attainmentSource: AttainmentSource.NoTarget);

        var stored = CalculationTraceSerializer.Serialize(new RuleCalculationTrace
        {
            CreditGenerated = evaluation.CreditGenerated,
            Commission = evaluation.Commission,
            Steps = steps,
        });

        // Enums are written as TEXT, so the stored document is greppable and JSON_VALUE-queryable
        // without the reader having to know an ordinal.
        stored.Should().Contain("NoTarget");
        stored.Should().Contain("Skipped");

        // And it round-trips, so a reader gets the facts back rather than a string match.
        var back = CalculationTraceSerializer.Deserialize(stored)!;
        var rate = back.Steps.Single(x => x.Component == RuleCalculationComponent.Rate);

        rate.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        rate.AttainmentSource.Should().Be(AttainmentSource.NoTarget);
        back.Commission!.Amount.Should().Be(0m);

        // ★ A CREDIT IS STILL CREATED. Zero-with-a-reason, not nothing: a missing row would be
        // indistinguishable from a transaction the pay run has not reached yet, and the whole point
        // is that somebody can find this one.
        back.CreditGenerated.Should().BeTrue();
    }

    /// <summary>The neighbour it must not be confused with, stored side by side.</summary>
    [Fact]
    public void A_measured_zero_stores_a_visibly_different_document()
    {
        var steps = new List<RuleCalculationStep>();
        CommissionCalculator.Evaluate(
            AttainmentRule(), Tx(50_000m), EUR, 0m, null, NullLogger.Instance,
            trace: steps, attainmentSource: AttainmentSource.Measured);

        var rate = steps.Single(x => x.Component == RuleCalculationComponent.Rate);

        rate.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        rate.AttainmentSource.Should().Be(AttainmentSource.Measured);
        rate.Output!.Amount.Should().Be(2000m, "a real 0% against a real quota still earns the base bracket");
    }
}
