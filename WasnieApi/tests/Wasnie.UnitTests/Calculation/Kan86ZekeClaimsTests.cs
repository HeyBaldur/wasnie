using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;
using Wasnie.Infrastructure.Compensation.Calculation;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// KAN-86 — the engine's own answer to each claim Zeke made about it.
///
/// ★★ EVERY AMOUNT HERE WAS READ OFF A RUN, NOT WORKED OUT BY HAND (§A6). The cases were first
/// printed by a throwaway probe and the numbers copied back; nothing below is an expectation of what
/// the engine ought to do. If one of these moves, somebody's commission moved with it.
///
/// ★★ WHY <c>Evaluate</c> IS THE RIGHT DOOR AND NOT A LEDGER QUERY. The ticket asks for the real
/// <c>CreditedAmount</c>. Between this method and that column there is one statement —
/// <c>CreditAllocationService.cs:389</c>, <c>Money.Of(commissionAmount.Amount, …)</c> — a defensive
/// copy that cannot change a number. So for the cascade (rate, modifier, cap, floor, trigger) this IS
/// the ledger figure. What it is NOT is the DATABASE half: whether the attainment the engine feeds in
/// is the one from before the sale is a question about <c>QuotaAttainmentService</c> and its cache,
/// and no unit test can honestly answer it. Those claims are marked below and left open.
///
/// ★ THE TWO PARTIES BEING JUDGED ARE ZEKE AND THE MANUAL, NOT THE ENGINE. Where the engine and a
/// claim disagree, the test records the ENGINE and the comment names who was wrong.
/// </summary>
public sealed class Kan86ZekeClaimsTests
{
    private const string EUR = "EUR";
    private static readonly DateOnly TxDate = new(2026, 3, 15);
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

    private static CompensationTransaction Tx(decimal amount, int quantity = 1) =>
        CompensationTransaction.Ingest(
            tenantId: Guid.NewGuid(), referenceNumber: "KAN-86", payeeId: Guid.NewGuid(),
            amount: Money.Of(amount, EUR), transactionDate: TxDate, source: TransactionSource.Manual,
            ingestedBy: "test", id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid(), quantity: quantity);

    private static Plan DraftPlan() => Plan.Create(
        tenantId: Guid.NewGuid(), name: "KAN-86 lab", description: "d",
        effectivePeriod: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
        currency: EUR, createdBy: "test", id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid());

    private static Rule Rule(
        RateTable table, Trigger? trigger = null, Modifier? modifier = null,
        Cap? cap = null, Floor? floor = null, MeasurementType measurement = MeasurementType.Revenue)
        => DraftPlan().AddRule(
            name: "R", sortOrder: 0, measurement: new Measurement { Type = measurement },
            rateTable: table, trigger: trigger, modifier: modifier, cap: cap, floor: floor);

    private static Cap CapOf(decimal amount, CapScope scope = CapScope.PerTransaction)
        => new() { Amount = Money.Of(amount, EUR), Scope = scope };

    private static Floor FloorOf(decimal amount) => new() { Amount = Money.Of(amount, EUR) };

    /// <summary>The two attainment tiers the ticket's cases use: 0–1 → 8%, above 1 → 12%.</summary>
    private static RateTable AttTiers(bool splitAtQuota = false) => RateTable.AttainmentBased(
        new[]
        {
            new AttainmentTier { AttainmentFrom = 0m, AttainmentTo = 1m, Rate = 0.08m },
            new AttainmentTier { AttainmentFrom = 1m, AttainmentTo = null, Rate = 0.12m },
        },
        splitAtQuota: splitAtQuota);

    private sealed record Result(
        decimal Commission, bool Credited, IReadOnlyList<RuleCalculationStep> Steps)
    {
        public RuleCalculationOutcome Outcome(RuleCalculationComponent c) =>
            Steps.Single(s => s.Component == c).Outcome;

        public decimal? Output(RuleCalculationComponent c) =>
            Steps.Single(s => s.Component == c).Output?.Amount;
    }

    private static Result Run(
        Rule rule, decimal amount, int quantity = 1,
        decimal attainmentPct = 1.0m,
        AttainmentSource source = AttainmentSource.Measured,
        AttainmentSplitContext? split = null)
    {
        var steps = new List<RuleCalculationStep>();
        var ev = CommissionCalculator.Evaluate(
            rule, Tx(amount, quantity), EUR, attainmentPct, split, NullLogger.Instance,
            trace: steps, attainmentSource: source);

        return new Result(ev.Commission.Amount, ev.CreditGenerated, steps);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // CLAIM 1 — «the floor can exceed the cap; if floor > cap, the floor wins». MONEY.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ ZEKE IS RIGHT, AND THIS IS THE CASE THAT COULD HAVE COST MONEY. Sale 100,000, flat 10%
    /// (=10,000), cap 8,000, floor 12,000. The cap lowers to 8,000 and the floor then LIFTS past it,
    /// because the floor runs after the cap and only ever raises (CommissionCalculator.cs:374-381).
    /// A reader who assumed the cap is an absolute ceiling would have budgeted 8,000 and paid 12,000.
    /// </summary>
    [Fact]
    public void Claim1_FloorAboveCap_TheFloorWins()
    {
        var r = Run(Rule(RateTable.Flat(0.10m), cap: CapOf(8000m), floor: FloorOf(12000m)), 100000m);

        r.Commission.Should().Be(12000m);
        // The cap did fire — it is not being skipped, it is being overridden afterwards.
        r.Outcome(RuleCalculationComponent.Cap).Should().Be(RuleCalculationOutcome.Applied);
        r.Output(RuleCalculationComponent.Cap).Should().Be(8000m);
    }

    /// <summary>The same rule without the floor, so the 12,000 above is unmistakably the floor's doing.</summary>
    [Fact]
    public void Claim1_CapAlone_Lowers()
        => Run(Rule(RateTable.Flat(0.10m), cap: CapOf(8000m)), 100000m).Commission.Should().Be(8000m);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // CLAIM 3 — the order is rate → modifier → cap → floor.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★ THE MODIFIER'S OUTPUT IS WHAT PROVES THE ORDER, NOT THE TOTAL. Rate 10,000, modifier ×2,
    /// cap 8,000, floor 12,000. Several orders happen to end at 12,000, so the assertion is on the
    /// intermediate steps: the modifier saw 10,000 and produced 20,000, and the cap then saw that
    /// 20,000 rather than the 10,000 the rate produced.
    /// </summary>
    [Fact]
    public void Claim3_TheCascadeRunsInOrder()
    {
        var r = Run(Rule(RateTable.Flat(0.10m),
            modifier: new Modifier { Type = ModifierType.Accelerator, Factor = 2m },
            cap: CapOf(8000m), floor: FloorOf(12000m)), 100000m);

        r.Output(RuleCalculationComponent.Rate).Should().Be(10000m);
        r.Output(RuleCalculationComponent.Modifier).Should().Be(20000m, "the modifier runs on the rate's output");
        r.Output(RuleCalculationComponent.Cap).Should().Be(8000m, "the cap runs on the modifier's output");
        r.Commission.Should().Be(12000m, "the floor runs last");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // CLAIM 4 — the cap reads the COMMISSION, not the sale amount.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Flat 1% on 100,000 is 1,000, under a cap of 8,000. Were the cap comparing itself with the SALE
    /// (100,000 > 8,000) it would clamp to 8,000. It does not, and the trace says so in its own words:
    /// the cap was applied and had no effect.
    /// </summary>
    [Fact]
    public void Claim4_TheCapComparesAgainstTheCommission()
    {
        var r = Run(Rule(RateTable.Flat(0.01m), cap: CapOf(8000m)), 100000m);

        r.Commission.Should().Be(1000m);
        r.Outcome(RuleCalculationComponent.Cap).Should().Be(RuleCalculationOutcome.AppliedWithoutEffect);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // CLAIM 10 — there is no period cap, and the DEFAULT scope is the one that does nothing.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Claim10_APeriodCapIsIgnored()
    {
        var r = Run(Rule(RateTable.Flat(0.10m), cap: CapOf(8000m, CapScope.PerPeriod)), 100000m);

        r.Commission.Should().Be(10000m, "only PerTransaction is honoured");
        r.Outcome(RuleCalculationComponent.Cap).Should().Be(RuleCalculationOutcome.Skipped);
    }

    /// <summary>
    /// ★★ A CAP BUILT WITHOUT NAMING ITS SCOPE IS A CAP THAT DOES NOTHING, because
    /// <c>default(CapScope)</c> is <c>PerPeriod</c> (CapScope.cs:6) — the value the engine skips.
    /// Nothing is broken today, and the guard is explicit rather than lucky: every write door refuses
    /// a non-per-transaction cap outright (AddRuleToPlanHandler.cs:31-33, UpdateRuleHandler.cs:31-33,
    /// SimulateRuleHandler.cs:55-58, all with "Only Per Transaction cap scope is currently
    /// supported."), and the form offers that scope alone. This pins the engine's own last line of
    /// defence, so a future path that reaches it without passing a handler fails HERE rather than in
    /// somebody's payslip.
    /// </summary>
    [Fact]
    public void Claim10_ACapWithNoScopeDefaultsToTheOneThatDoesNothing()
    {
        default(CapScope).Should().Be(CapScope.PerPeriod);

        var r = Run(Rule(RateTable.Flat(0.10m), cap: new Cap { Amount = Money.Of(8000m, EUR) }), 100000m);

        r.Commission.Should().Be(10000m);
        r.Outcome(RuleCalculationComponent.Cap).Should().Be(RuleCalculationOutcome.Skipped);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // CLAIM 2 — «Split OFF uses the attainment from BEFORE the deal». MONEY.
    //
    // ★ WHAT THIS PROVES AND WHAT IT DOES NOT. It proves the ARITHMETIC: fed 0.90 the engine charges
    // the 8% tier over the whole sale, which is Zeke's 2,400 — and fed the post-sale ratio it would
    // charge 3,600 instead, so the two readings genuinely differ by 1,200 on one deal. Which of the
    // two the engine actually feeds in is decided by QuotaAttainmentService against the database and
    // is NOT settled here.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Claim2_PriorAttainmentChargesTheLowerTierOverTheWholeSale()
        => Run(Rule(AttTiers()), 30000m, attainmentPct: 0.90m).Commission.Should().Be(2400m);

    [Fact]
    public void Claim2_TheRivalReadingWouldPay1200More()
        => Run(Rule(AttTiers()), 30000m, attainmentPct: 1.20m).Commission.Should().Be(3600m);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // THE 100% BOUNDARY — the manual does not state it, and this is the answer.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ AT EXACTLY 1.00 THE UPPER TIER WINS. Both tiers claim the ratio — [0, 1] by its closed top
    /// and [1, null] by its floor — and <c>FindAttainmentBracket</c> settles it with
    /// <c>LastOrDefault</c> (CommissionCalculator.cs:240-246). 30,000 at 12%, not at 8%: on this
    /// single sale the boundary is worth 1,200.
    /// </summary>
    [Fact]
    public void Boundary_AttainmentExactlyOne_TakesTheUpperTier()
        => Run(Rule(AttTiers()), 30000m, attainmentPct: 1.00m).Commission.Should().Be(3600m);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // CLAIM 6 — Split ON: one credit or two? Zeke did not say.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ ONE COMMISSION, THEREFORE ONE CREDIT. Quota 100,000, prior 90,000, sale 30,000: the walk
    /// charges 10,000 at 8% and 20,000 at 12% and hands back their SUM — 3,200 — as a single Rate
    /// step. The split lives inside the rate, not in the credit, so the ledger gets one row per rule
    /// whether the setting is on or off; the tier-by-tier breakdown survives in the trace.
    /// </summary>
    [Fact]
    public void Claim6_SplitAtQuotaProducesOneCommission_NotTwo()
    {
        var r = Run(Rule(AttTiers(splitAtQuota: true)), 30000m,
            split: new AttainmentSplitContext(90000m, 100000m));

        r.Commission.Should().Be(3200m, "10,000 at 8% plus 20,000 at 12%");
        r.Steps.Count(s => s.Component == RuleCalculationComponent.Rate).Should().Be(1);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // CLAIM 7 — a trigger that does not match creates NO credit, not a credit of zero.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★ AND THE FLOOR DOES NOT RESCUE IT. The rule carries a floor of 500; a trigger that never
    /// matches stops the cascade before any of that runs, so there is no credit for the floor to
    /// raise. <c>CreditGenerated</c> is the discriminator — the amount alone could not tell this
    /// apart from a rule that matched and computed nothing.
    /// </summary>
    [Fact]
    public void Claim7_TriggerNotMatched_NoCreditAtAll()
    {
        var never = Trigger.When(LogicalOperator.And, new[]
        {
            new Condition
            {
                Field = "transactionamount",
                Operator = ConditionOperator.GreaterThan,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "999999999" },
            },
        });

        var r = Run(Rule(RateTable.Flat(0.10m), trigger: never, floor: FloorOf(500m)), 100000m);

        r.Credited.Should().BeFalse();
        r.Commission.Should().Be(0m);
        r.Steps.Should().ContainSingle().Which.Outcome.Should().Be(RuleCalculationOutcome.NotMatched);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // THE EXCEPTION NOBODY DOCUMENTED — a refused rate suppresses the floor.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ «THE FLOOR ALWAYS LIFTS» IS FALSE, AND THE EXCEPTION IS DELIBERATE. An attainment rule with
    /// no quota in effect refuses to produce a rate, and a refusal suppresses the floor
    /// (CommissionCalculator.cs:888-921): a floor is the minimum commission ON a commissioned sale,
    /// so with no commission to be the minimum of, paying it would be an orphan. The credit exists
    /// and is zero — which is not the same as no credit (claim 7 above).
    /// </summary>
    [Fact]
    public void RefusedRate_SuppressesTheFloor()
    {
        var r = Run(Rule(AttTiers(), floor: FloorOf(500m)), 30000m,
            attainmentPct: 0m, source: AttainmentSource.NoTarget);

        r.Credited.Should().BeTrue("the rule matched — it simply could not calculate");
        r.Commission.Should().Be(0m, "not 500: the floor is suppressed, not applied");
        r.Outcome(RuleCalculationComponent.Floor).Should().Be(RuleCalculationOutcome.Skipped);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // CLAIM 9 — «Tiered: Revenue only. Attainment: Revenue only.»
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★ THE INVARIANT RUNS THE OTHER WAY. What the domain refuses is UNITS with anything but Flat
    /// (Rule.cs:71-74); it does not say that tiered and attainment tables are Revenue-only. Margin is
    /// accepted, and the engine then prices it on <c>transaction.Amount</c> like Revenue
    /// (CommissionCalculator.cs:554). Not reachable from the form — it offers Revenue and Units only —
    /// so this pins the gap rather than reporting a live defect.
    /// </summary>
    [Fact]
    public void Claim9_UnitsRequiresFlat_ButTieredIsNotRevenueOnly()
    {
        var withAttainment = Record.Exception(() =>
            Rule(AttTiers(), measurement: MeasurementType.Units));
        withAttainment.Should().BeOfType<DomainException>();

        var withTiered = Record.Exception(() => Rule(
            RateTable.Tiered(new[] { new RateTier { From = 0m, To = null, Rate = 0.05m } }),
            measurement: MeasurementType.Units));
        withTiered.Should().BeOfType<DomainException>();

        // The claim's other half does not hold: nothing refuses this.
        Record.Exception(() => Rule(AttTiers(), measurement: MeasurementType.Margin))
            .Should().BeNull();
    }

    /// <summary>
    /// ★★ IN UNITS THE FLAT RATE IS MONEY PER UNIT AND THE SALE AMOUNT IS IGNORED ENTIRELY. 50 with
    /// Units means €50 a unit; three units pay 150, and the 100,000 on the transaction plays no part.
    /// This is exactly the confusion the ticket reports Zeke causing by putting «0.50» and «€50 per
    /// unit» in one example: the same field is a FRACTION in Revenue and a PRICE in Units (§C4).
    /// </summary>
    [Fact]
    public void Claim9_UnitsFlatRateIsMoneyPerUnit_NotAFraction()
        => Run(Rule(RateTable.Flat(50m), measurement: MeasurementType.Units), 100000m, quantity: 3)
            .Commission.Should().Be(150m);
}
