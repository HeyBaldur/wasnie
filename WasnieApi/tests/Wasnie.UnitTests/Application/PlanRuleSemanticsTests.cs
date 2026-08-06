using FluentAssertions;
using Wasnie.Application.Assistant.Tools;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// THE TOKENS THE ASSISTANT IS TAUGHT, CHECKED AGAINST THE ENGINE THAT ACTUALLY PAYS PEOPLE.
///
/// ★ WHY THESE TESTS RUN THE REAL CALCULATOR. <see cref="PlanRuleSemantics"/> is a mirror of
/// <c>CommissionCalculator</c> — it has to be, because the calculator is internal to another assembly —
/// and a mirror that drifts is worse than no mirror at all: the assistant would confidently explain a
/// calculation the engine stopped performing, to somebody asking why they were paid what they were
/// paid. So the assertions below are not a second copy of the expectation. Each one executes the REAL
/// engine method and checks that its arithmetic is what the token claims. Change the engine's semantics
/// and the token that describes them goes red.
///
/// ★ AND THE ENUM IS CLOSED. The exhaustiveness test walks every combination the type system allows and
/// demands a token for each. A new <c>RateTableType</c> or <c>MeasurementType</c> member fails here
/// rather than being quietly folded into the nearest existing meaning.
/// </summary>
public sealed class PlanRuleSemanticsTests
{
    private const string Eur = "EUR";

    // ── Each token, against the engine it describes ───────────────────────────

    [Fact]
    public void FractionalMultiplierOfBase_IS_what_a_flat_revenue_rule_does()
    {
        PlanRuleSemantics.Describe(RateTableType.Flat, MeasurementType.Revenue, splitAtQuota: false)
            .Should().Be(RateSemantic.FractionalMultiplierOfBase);

        // The token says: rawValue is a FRACTION of the base. 10.000 × 0.05 = 500 — not 0.05 currency
        // units, and not 5 × the base.
        var commission = CommissionCalculator.ComputeCommission(
            Money.Of(10_000m, Eur), RateTable.Flat(0.05m), attainmentPct: 1m);

        commission.Amount.Should().Be(500m);
        commission.Currency.Should().Be(Eur);
    }

    [Fact]
    public void CurrencyAmountPerUnit_IS_what_a_flat_units_rule_does()
    {
        PlanRuleSemantics.Describe(RateTableType.Flat, MeasurementType.Units, splitAtQuota: false)
            .Should().Be(RateSemantic.CurrencyAmountPerUnit);

        // The token says: rawValue is MONEY PER UNIT. 2.00 × 10 units = 20.00. The transaction's amount
        // never enters the calculation — which is the fact the assistant got wrong when it explained a
        // per-unit rate as a percentage.
        var commission = CommissionCalculator.ComputeUnitsCommission(
            quantity: 10, ratePerUnit: 2.00m, currency: Eur);

        commission.Amount.Should().Be(20m);
    }

    [Fact]
    public void FractionalRatePerRevenueBracket_IS_progressive_not_a_single_rate()
    {
        PlanRuleSemantics.Describe(RateTableType.Tiered, MeasurementType.Revenue, splitAtQuota: false)
            .Should().Be(RateSemantic.FractionalRatePerRevenueBracket);

        // The token says: each bracket's rate applies to the PORTION of the base inside it.
        // 10.000 over [0,5.000)@2% + [5.000,∞)@10% = 100 + 500 = 600.
        // A single-rate reading of the same table would say 10.000 × 10% = 1.000 — the assertion is
        // what separates the two.
        var commission = CommissionCalculator.ComputeTieredCommission(
            Money.Of(10_000m, Eur),
            [
                new RateTier { From = 0m, To = 5_000m, Rate = 0.02m },
                new RateTier { From = 5_000m, To = null, Rate = 0.10m },
            ]);

        commission.Amount.Should().Be(600m);
    }

    [Fact]
    public void FractionalMultiplierFromAttainmentBracket_pays_ONE_rate_on_the_WHOLE_base()
    {
        PlanRuleSemantics.Describe(RateTableType.AttainmentBased, MeasurementType.Revenue, splitAtQuota: false)
            .Should().Be(RateSemantic.FractionalMultiplierFromAttainmentBracket);

        // The token says: the bracket containing the attainment supplies one fraction, applied to
        // everything. At 120% attainment the 1.00+ bracket pays 12% on the whole 10.000 = 1.200.
        var commission = CommissionCalculator.ComputeAttainmentCommission(
            Money.Of(10_000m, Eur),
            [
                new AttainmentTier { AttainmentFrom = 0m, AttainmentTo = 1.00m, Rate = 0.05m },
                new AttainmentTier { AttainmentFrom = 1.00m, AttainmentTo = null, Rate = 0.12m },
            ],
            attainmentPct: 1.20m);

        commission.Amount.Should().Be(1_200m);
    }

    [Fact]
    public void FractionalRateSplitAtQuotaBoundary_pays_each_bracket_on_its_OWN_slice()
    {
        PlanRuleSemantics.Describe(RateTableType.AttainmentBased, MeasurementType.Revenue, splitAtQuota: true)
            .Should().Be(RateSemantic.FractionalRateSplitAtQuotaBoundary);

        // The token says: the transaction is SPLIT at the quota boundary. Quota 100.000, already at
        // 90.000, this deal is 20.000 — so 10.000 sits below quota at 5% (500) and 10.000 above it at
        // 12% (1.200) = 1.700. The bracket-lookup token above would have said 20.000 × 12% = 2.400 for
        // the same rep on the same deal; two different answers, which is exactly why they are two
        // different tokens.
        var commission = CommissionCalculator.ComputeAttainmentSplitCommission(
            Money.Of(20_000m, Eur),
            [
                new AttainmentTier { AttainmentFrom = 0m, AttainmentTo = 1.00m, Rate = 0.05m },
                new AttainmentTier { AttainmentFrom = 1.00m, AttainmentTo = null, Rate = 0.12m },
            ],
            priorCumulative: 90_000m,
            quotaTarget: 100_000m);

        commission.Amount.Should().Be(1_700m);
    }

    [Theory]
    [InlineData(RateTableType.Tiered)]
    [InlineData(RateTableType.AttainmentBased)]
    public void NoCommissionUnsupportedCombination_IS_units_with_a_rate_table_the_engine_refuses(
        RateTableType rateTable)
    {
        PlanRuleSemantics.Describe(rateTable, MeasurementType.Units, splitAtQuota: false)
            .Should().Be(RateSemantic.NoCommissionUnsupportedCombination);

        // The domain refuses to SAVE this combination at all — which is the engine's own statement that
        // it cannot calculate it, and the reason the token promises zero rather than a number.
        var create = () => Wasnie.Domain.Compensation.Plans.Plan
            .Create(
                Guid.NewGuid(), "Units plan", string.Empty,
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Eur, "test", Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid())
            .AddRule(
                "Per unit", 0,
                new Measurement { Type = MeasurementType.Units },
                rateTable == RateTableType.Tiered
                    ? RateTable.Tiered([new RateTier { From = 0m, To = null, Rate = 0.05m }])
                    : RateTable.AttainmentBased([new AttainmentTier { AttainmentFrom = 0m, Rate = 0.05m }]));

        create.Should().Throw<Wasnie.Domain.Exceptions.DomainException>();
    }

    // ── The measurement base, which is NOT the measurement's name ─────────────

    [Theory]
    [InlineData(MeasurementType.Revenue)]
    [InlineData(MeasurementType.Margin)]
    [InlineData(MeasurementType.Attainment)]
    [InlineData(MeasurementType.Custom)]
    public void Everything_that_is_not_Units_is_calculated_on_the_transaction_AMOUNT(MeasurementType type)
    {
        // ★ THE SURPRISE THE TOKEN EXISTS TO SURFACE. CreditAllocationService branches on Units alone:
        // a rule labelled "Margin" is paid on gross transaction amount. Reporting the LABEL would let
        // the assistant describe a margin calculation nobody implemented.
        PlanRuleSemantics.BaseOf(type).Should().Be(MeasurementBase.TransactionAmount);
    }

    [Fact]
    public void Units_is_calculated_on_the_transaction_QUANTITY()
    {
        PlanRuleSemantics.BaseOf(MeasurementType.Units).Should().Be(MeasurementBase.TransactionQuantity);
    }

    // ── ★ THE ENUM IS CLOSED ──────────────────────────────────────────────────

    [Fact]
    public void EVERY_combination_the_domain_allows_has_its_own_token()
    {
        foreach (MeasurementType measurement in Enum.GetValues<MeasurementType>())
        {
            foreach (RateTableType rateTable in Enum.GetValues<RateTableType>())
            {
                foreach (var split in new[] { true, false })
                {
                    var describe = () => PlanRuleSemantics.Describe(rateTable, measurement, split);

                    describe.Should().NotThrow(
                        $"({rateTable}, {measurement}, split={split}) is a rule an administrator can " +
                        "save, so the assistant must have a token for what it means");
                }
            }
        }
    }

    [Fact]
    public void A_rate_table_type_with_NO_token_is_refused_rather_than_guessed()
    {
        // ★ The failure mode this prevents: a new rate mode shipped, the mapper falls through to
        // "fraction of base", and the assistant explains a rate convention the engine never had. The
        // throw becomes a retry card, which is honest; the guess would be a number in front of a user
        // who believes it.
        var describe = () => PlanRuleSemantics.Describe(
            (RateTableType)99, MeasurementType.Revenue, splitAtQuota: false);

        describe.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void A_measurement_type_with_NO_token_is_refused_rather_than_assumed_to_be_revenue()
    {
        var baseOf = () => PlanRuleSemantics.BaseOf((MeasurementType)99);

        baseOf.Should().Throw<NotSupportedException>();
    }
}
