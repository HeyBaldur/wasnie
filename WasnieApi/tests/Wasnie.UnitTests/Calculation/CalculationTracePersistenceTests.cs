using System.Text.Json;
using FluentAssertions;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence.Serialization;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// THE STORED FORM OF THE TRACE (KAN-27).
///
/// ★★ WHAT IS ACTUALLY AT STAKE. The engine has been able to narrate itself for a while; production
/// threw the narration away. The reason that matters is that the narration is NOT reproducible
/// later: quota attainment is as-of-a-date and keeps moving, so re-running the engine in November
/// answers a different question about March and answers it confidently. A trace not captured at
/// allocation is a payment nobody can ever explain again.
///
/// ★ THESE TESTS ARE ABOUT THE DOCUMENT, NOT THE MONEY. Not one amount is asserted here — the
/// amounts are pinned in CommissionEngineCharacterizationTests, written against the untouched engine
/// and not edited by this work item.
/// </summary>
public sealed class CalculationTracePersistenceTests
{
    private const string EUR = "EUR";

    private static RuleCalculationTrace TraceWith(params RuleCalculationStep[] steps) => new()
    {
        CreditGenerated = true,
        Commission = Money.Of(8_520m, EUR),
        Steps = steps,
    };

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

    // ── The shape ────────────────────────────────────────────────────────────

    [Fact]
    public void TheDocumentDeclaresItsSchema()
    {
        var json = CalculationTraceSerializer.Serialize(TraceWith());

        Root(json).GetProperty("_schema").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// ★★ THE DECISION THIS TEST EXISTS TO HOLD. Persisting the enums by ordinal would tie years of
    /// stored history to today's declaration order: insert one member into RuleCalculationOutcome and
    /// every trace ever written is silently reinterpreted — "the cap was skipped" becomes "the cap
    /// applied" — with nothing to notice it by. Text cannot do that.
    /// </summary>
    [Fact]
    public void OutcomesAreStoredAsTextAndNeverAsNumbers()
    {
        var json = CalculationTraceSerializer.Serialize(TraceWith(new RuleCalculationStep
        {
            Component = RuleCalculationComponent.Cap,
            Outcome = RuleCalculationOutcome.Skipped,
        }));

        var step = Root(json).GetProperty("steps")[0];
        step.GetProperty("component").GetString().Should().Be("Cap");
        step.GetProperty("outcome").GetString().Should().Be("Skipped");
    }

    /// <summary>
    /// The column lives on Credits, so the row already says which credit, transaction, plan and rule
    /// this is. Repeating any of that inside the document would create a second answer to a question
    /// that already has one — and the two can disagree.
    /// </summary>
    [Fact]
    public void TheDocumentCarriesNoIdentifiers()
    {
        var json = CalculationTraceSerializer.Serialize(TraceWith(new RuleCalculationStep
        {
            Component = RuleCalculationComponent.Rate,
            Outcome = RuleCalculationOutcome.Applied,
            Input = Money.Of(1000m, EUR),
            Output = Money.Of(50m, EUR),
        }));

        foreach (var forbidden in new[] { "creditId", "transactionId", "payeeId", "planId", "ruleId" })
        {
            json.Should().NotContain(forbidden);
        }
    }

    // ── Round trip ───────────────────────────────────────────────────────────

    [Fact]
    public void ARoundTripPreservesTheCascadeInOrder()
    {
        var original = TraceWith(
            new RuleCalculationStep { Component = RuleCalculationComponent.Trigger, Outcome = RuleCalculationOutcome.Applied },
            new RuleCalculationStep
            {
                Component = RuleCalculationComponent.Rate,
                Outcome = RuleCalculationOutcome.Applied,
                Input = Money.Of(10_000m, EUR),
                Output = Money.Of(750m, EUR),
                Operand = 0.075m,
                RateTable = RateTableType.Flat,
            },
            new RuleCalculationStep
            {
                Component = RuleCalculationComponent.Cap,
                Outcome = RuleCalculationOutcome.AppliedWithoutEffect,
                Input = Money.Of(750m, EUR),
                Output = Money.Of(750m, EUR),
                Threshold = Money.Of(7_850m, EUR),
            },
            new RuleCalculationStep
            {
                Component = RuleCalculationComponent.Floor,
                Outcome = RuleCalculationOutcome.Applied,
                Input = Money.Of(750m, EUR),
                Output = Money.Of(8_520m, EUR),
                Threshold = Money.Of(8_520m, EUR),
            });

        var back = CalculationTraceSerializer.Deserialize(
            CalculationTraceSerializer.Serialize(original))!;

        back._schema.Should().Be(1);
        back.CreditGenerated.Should().BeTrue();
        back.Steps.Select(s => s.Component).Should().Equal(
            RuleCalculationComponent.Trigger,
            RuleCalculationComponent.Rate,
            RuleCalculationComponent.Cap,
            RuleCalculationComponent.Floor);

        var floor = back.Steps.Single(s => s.Component == RuleCalculationComponent.Floor);
        floor.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        floor.Input!.Amount.Should().Be(750m);
        floor.Output!.Amount.Should().Be(8_520m);
        floor.Threshold!.Amount.Should().Be(8_520m);

        var rate = back.Steps.Single(s => s.Component == RuleCalculationComponent.Rate);
        rate.Operand.Should().Be(0.075m);
        rate.RateTable.Should().Be(RateTableType.Flat);
    }

    /// <summary>
    /// ★ THE CAP/FLOOR ORDER IS THE POINT OF THE WHOLE FEATURE, so it has to survive storage. Cap
    /// before floor means a floor above a cap wins and the rule pays more than its own ceiling —
    /// anybody reconstructing the cascade from the rule's fields would get a different number.
    /// </summary>
    [Fact]
    public void TheStoredOrderIsTheOrderTheEngineRan_CapBeforeFloor()
    {
        var json = CalculationTraceSerializer.Serialize(TraceWith(
            new RuleCalculationStep { Component = RuleCalculationComponent.Cap, Outcome = RuleCalculationOutcome.Skipped },
            new RuleCalculationStep { Component = RuleCalculationComponent.Floor, Outcome = RuleCalculationOutcome.Applied }));

        var steps = Root(json).GetProperty("steps");
        steps[0].GetProperty("component").GetString().Should().Be("Cap");
        steps[1].GetProperty("component").GetString().Should().Be("Floor");
    }

    [Fact]
    public void TieredWalksSurviveStorage()
    {
        var original = TraceWith(new RuleCalculationStep
        {
            Component = RuleCalculationComponent.Rate,
            Outcome = RuleCalculationOutcome.Applied,
            RateTable = RateTableType.Tiered,
            Tiers =
            [
                new RateTierStep(0m, 20_000m, 0.04m, 20_000m, Money.Of(800m, EUR)),
                new RateTierStep(20_000m, null, 0.06m, 5_000m, Money.Of(300m, EUR)),
            ],
        });

        var back = CalculationTraceSerializer.Deserialize(
            CalculationTraceSerializer.Serialize(original))!;

        var tiers = back.Steps.Single().Tiers!;
        tiers.Should().HaveCount(2);
        tiers[0].Rate.Should().Be(0.04m);
        tiers[1].To.Should().BeNull("the open top tier is what stops overachievers earning nothing");
        tiers[1].Amount.Amount.Should().Be(300m);
    }

    // ── Forward compatibility ────────────────────────────────────────────────

    /// <summary>
    /// ★★ A LATER VERSION'S TRACE MUST NOT TAKE A BREAKDOWN OFFLINE. A document written by a build
    /// that knows an outcome this one has never heard of still has to parse: refusing the whole
    /// document over one unfamiliar word would hide a payment explanation because of vocabulary.
    /// </summary>
    [Fact]
    public void ADocumentWithAnUnknownFieldStillReads()
    {
        const string fromTheFuture = """
        {
          "_schema": 1,
          "creditGenerated": true,
          "commission": { "amount": 100.00, "currency": "EUR" },
          "somethingNobodyHasInventedYet": { "nested": [1, 2, 3] },
          "steps": [
            { "component": "Floor", "outcome": "Applied", "aBrandNewField": 42 }
          ]
        }
        """;

        var back = CalculationTraceSerializer.Deserialize(fromTheFuture)!;

        back.CreditGenerated.Should().BeTrue();
        back.Steps.Should().ContainSingle()
            .Which.Outcome.Should().Be(RuleCalculationOutcome.Applied);
    }

    /// <summary>
    /// Null in, null out — and that is a real answer, not a failure. The 1,296 credits allocated
    /// before this column existed have no trace and never will: the inputs are gone, so a backfill
    /// could only invent them. "We did not record this" must not be confused with an empty cascade,
    /// which would read as an engine that ran and did nothing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AHistoricCreditWithNoTraceReadsAsNull(string? stored)
    {
        CalculationTraceSerializer.Deserialize(stored).Should().BeNull();
    }

    // ── The entity ───────────────────────────────────────────────────────────

    private static Credit Allocate(string? trace) => Credit.Allocate(
        tenantId: Guid.NewGuid(),
        transactionId: Guid.NewGuid(),
        payeeId: Guid.NewGuid(),
        planId: Guid.NewGuid(),
        ruleId: Guid.NewGuid(),
        ruleSnapshot: RuleSnapshot.Freeze(
            Guid.NewGuid(), Guid.NewGuid(), 1, "R", RateTable.Flat(0.05m), Trigger.Always(),
            DateTimeOffset.UtcNow, measurement: new Measurement { Type = MeasurementType.Revenue }),
        originalAmount: Money.Of(1000m, EUR),
        creditedAmount: Money.Of(50m, EUR),
        splitPercentage: Percentage.FromPercent(100),
        role: CreditRole.Primary,
        allocatedBy: "test",
        id: Guid.NewGuid(),
        now: DateTimeOffset.UtcNow,
        eventId: Guid.NewGuid(),
        calculationTrace: trace);

    [Fact]
    public void ACreditCarriesTheDocumentItWasAllocatedWith()
    {
        var json = CalculationTraceSerializer.Serialize(TraceWith());

        Allocate(json).CalculationTrace.Should().Be(json);
    }

    [Fact]
    public void ACreditAllocatedWithoutAnEngineRunHasNoTrace()
    {
        Allocate(null).CalculationTrace.Should().BeNull();
    }

    /// <summary>
    /// ★ EVIDENCE, NOT STATE. A recalculation supersedes a credit and writes a new one; the old row
    /// keeps its own account of how it was computed, or the supersede erases the very thing somebody
    /// would be disputing. Nothing may set this back to null — asserted against the type, so a later
    /// "clear the trace" convenience fails here rather than in production.
    /// </summary>
    [Fact]
    public void NothingCanRewriteOrClearAStoredTrace()
    {
        var setter = typeof(Credit).GetProperty(nameof(Credit.CalculationTrace))!.SetMethod!;
        setter.IsPrivate.Should().BeTrue();

        // Named methods only: the property's own compiler-generated accessors are excluded, because
        // the assertion above is already what pins those. What must not exist is a DELIBERATE
        // mutator — a ClearTrace/SetTrace/UpdateTrace convenience somebody adds later.
        var named = typeof(Credit)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToList();

        named.Should().NotContain(n => n.Contains("Trace", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SupersedingACreditLeavesItsTraceIntact()
    {
        var json = CalculationTraceSerializer.Serialize(TraceWith());
        var credit = Allocate(json);

        credit.Supersede("recalculated", DateTimeOffset.UtcNow, Guid.NewGuid());

        credit.SupersededAt.Should().NotBeNull();
        credit.CalculationTrace.Should().Be(json, "the disputed row must keep its own account");
    }
}
