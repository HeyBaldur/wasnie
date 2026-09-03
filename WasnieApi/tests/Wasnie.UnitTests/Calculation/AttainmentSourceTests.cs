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
/// WHERE THE ATTAINMENT PERCENTAGE CAME FROM (KAN-27's prerequisite).
///
/// ★★ THE BUG THIS CLOSES IS A SENTENCE THE PRODUCT COULD NOT HELP TELLING. A zero attainment means
/// either "this rep achieved none of their target" or "nobody ever set a target" — a fact about a
/// person and a configuration hole, identical in the stored number and opposite in meaning. Every
/// one of them arrived sealed as <c>Measured</c>, because <c>Evaluate</c> defaults that parameter and
/// the ingestion path never passed it. So a breakdown could state, as measured fact, that somebody
/// achieved 0% of a quota that never existed.
///
/// ★ AND IT IS NOT DERIVABLE AFTERWARDS. A genuine 0% against a real target is also 0. The only place
/// the difference is knowable is where the quota was looked up, which is why the source now travels
/// with the ratio instead of being inferred from it.
/// </summary>
public sealed class AttainmentSourceTests
{
    private const string EUR = "EUR";
    private static readonly DateOnly TxDate = new(2026, 3, 15);
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

    private static CompensationTransaction Tx(decimal amount) =>
        CompensationTransaction.Ingest(
            tenantId: Guid.NewGuid(),
            referenceNumber: "REF-ATT",
            payeeId: Guid.NewGuid(),
            amount: Money.Of(amount, EUR),
            transactionDate: TxDate,
            source: TransactionSource.Manual,
            ingestedBy: "test",
            id: Guid.NewGuid(),
            now: Now,
            eventId: Guid.NewGuid(),
            quantity: 1);

    private static Rule AttainmentRule(bool splitAtQuota = false)
    {
        var plan = Plan.Create(
            tenantId: Guid.NewGuid(), name: "P", description: "d",
            effectivePeriod: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            currency: EUR, createdBy: "test", id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid());

        var table = RateTable.AttainmentBased(
            [
                new AttainmentTier { AttainmentFrom = 0m, AttainmentTo = 1m, Rate = 0.04m },
                new AttainmentTier { AttainmentFrom = 1m, AttainmentTo = null, Rate = 0.08m },
            ],
            splitAtQuota: splitAtQuota);

        return plan.AddRule(
            name: "Attainment rule", sortOrder: 0,
            measurement: new Measurement { Type = MeasurementType.Revenue },
            rateTable: table);
    }

    private static RuleCalculationStep RateStep(List<RuleCalculationStep> steps) =>
        steps.Single(s => s.Component == RuleCalculationComponent.Rate);

    private static List<RuleCalculationStep> Run(
        Rule rule, decimal attainmentPct, AttainmentSource source,
        AttainmentSplitContext? split = null)
    {
        var steps = new List<RuleCalculationStep>();
        CommissionCalculator.Evaluate(
            rule, Tx(10_000m), EUR, attainmentPct, split, NullLogger.Instance,
            trace: steps, attainmentSource: source);
        return steps;
    }

    // ── The bracket path ─────────────────────────────────────────────────────

    /// <summary>Edge case 1 of the ticket: no quota in effect must never read as measured.</summary>
    [Fact]
    public void NoQuotaInEffect_IsNoTarget_AndTheRatioIsZero()
    {
        var steps = Run(AttainmentRule(), attainmentPct: 0m, AttainmentSource.NoTarget);

        var rate = RateStep(steps);
        rate.AttainmentSource.Should().Be(AttainmentSource.NoTarget);
        rate.Operand.Should().Be(0m, "the percentage used is recorded alongside its source");
    }

    /// <summary>
    /// ★ THE CASE THAT MAKES NoTarget IMPOSSIBLE TO DERIVE. This zero and the one above are the same
    /// number and different facts, so a reader that inferred "0 means no quota" would erase a real,
    /// and bad, quarter.
    /// </summary>
    [Fact]
    public void AGenuineZeroAgainstARealTarget_StaysMeasured()
    {
        var rate = RateStep(Run(AttainmentRule(), attainmentPct: 0m, AttainmentSource.Measured));

        rate.AttainmentSource.Should().Be(AttainmentSource.Measured);
        rate.Operand.Should().Be(0m);
    }

    [Fact]
    public void AMeasuredAttainment_IsRecordedWithThePercentageUsed()
    {
        var rate = RateStep(Run(AttainmentRule(), attainmentPct: 0.7543m, AttainmentSource.Measured));

        rate.AttainmentSource.Should().Be(AttainmentSource.Measured);
        rate.Operand.Should().Be(0.7543m);
    }

    /// <summary>
    /// The engine's own fallback is 1.0 — a rep at exactly full quota, a figure that looks entirely
    /// reasonable and is false for almost everybody. It must never be presented as measured.
    /// </summary>
    [Fact]
    public void TheEnginesOwnDefault_IsRecordedAsDefaulted_NotMeasured()
    {
        var rate = RateStep(Run(AttainmentRule(), attainmentPct: 1.0m, AttainmentSource.Defaulted));

        rate.AttainmentSource.Should().Be(AttainmentSource.Defaulted);
        rate.Operand.Should().Be(1.0m);
    }

    // ── The split-at-quota path ──────────────────────────────────────────────

    /// <summary>
    /// ★ THIS PATH USED TO EMIT NO SOURCE AT ALL, which read as "not an attainment rule" — the step
    /// was silent about the very thing it depended on.
    /// </summary>
    [Fact]
    public void SplitAtQuota_WithNoQuota_IsNoTarget()
    {
        var rate = RateStep(Run(
            AttainmentRule(splitAtQuota: true), attainmentPct: 1.0m,
            AttainmentSource.Defaulted, split: null));

        rate.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        rate.AttainmentSource.Should().Be(AttainmentSource.NoTarget,
            "a null split context IS 'no quota configured' — the commission is zero for that reason");
    }

    [Fact]
    public void SplitAtQuota_WithAQuota_IsMeasured_AndRecordsTheRatioUsed()
    {
        var rate = RateStep(Run(
            AttainmentRule(splitAtQuota: true), attainmentPct: 1.0m, AttainmentSource.Defaulted,
            split: new AttainmentSplitContext(PriorCumulative: 5_000m, QuotaTarget: 10_000m)));

        rate.AttainmentSource.Should().Be(AttainmentSource.Measured);
        rate.Operand.Should().Be(0.5m, "half the quota had already been reached before this sale");
    }

    // ── The contract ─────────────────────────────────────────────────────────

    /// <summary>
    /// ★ APPENDED, NEVER INSERTED. Some clients could read this enum by ordinal, so reordering would
    /// silently reinterpret every value that ever crossed the wire. This pins the positions.
    /// </summary>
    [Fact]
    public void AttainmentSourceMembersKeepTheirPositions()
    {
        ((int)AttainmentSource.Measured).Should().Be(0);
        ((int)AttainmentSource.Supplied).Should().Be(1);
        ((int)AttainmentSource.Defaulted).Should().Be(2);
        ((int)AttainmentSource.NoTarget).Should().Be(3);
    }

    /// <summary>A non-attainment rule has no attainment to report, and must not invent one.</summary>
    [Fact]
    public void AFlatRule_ReportsNoAttainmentSourceAtAll()
    {
        var plan = Plan.Create(
            tenantId: Guid.NewGuid(), name: "P", description: "d",
            effectivePeriod: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            currency: EUR, createdBy: "test", id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid());

        var flat = plan.AddRule(
            name: "Flat", sortOrder: 0,
            measurement: new Measurement { Type = MeasurementType.Revenue },
            rateTable: RateTable.Flat(0.05m));

        RateStep(Run(flat, 1.0m, AttainmentSource.Measured)).AttainmentSource.Should().BeNull();
    }
}
