using System.Text.Json;
using FluentAssertions;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence.Serialization;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// KAN-28 tanda A — the queryable projection of the rate refusal onto <c>Credits.RateRefusal</c>.
///
/// ★★ THE COLUMN IS AN INDEX, NOT A SECOND TRUTH. The trace stays the evidence; the column exists
/// only so a reconciliation query can filter and GROUP BY without scanning nvarchar(max). The danger
/// of any denormalisation is that the two drift, so the rule is that BOTH are produced from the SAME
/// <see cref="RuleCalculationTrace"/> object in the same act, and the tests below assert that what
/// the column says is exactly what the stored document says — parsed back out of the JSON, not read
/// from the object that wrote it.
///
/// ★ THE SHAPE OF THE DOCUMENT IS FROZEN HERE ON PURPOSE. Tanda B reads the column, but anything
/// auditing a credit still reads the JSON, and the two have to keep agreeing. A change to the
/// document's field names or enum spelling turns this file red, which is the point.
/// </summary>
public sealed class CreditRefusalProjectionTests
{
    private const string EUR = "EUR";

    /// <summary>
    /// ★ VERBATIM FROM Credits.CalculationTrace IN THE LOCAL DATABASE — one of the five NoTarget rows
    /// KAN-26 tanda 2 produced. Note what it does NOT have: a <c>rateRefusal</c> field. It predates
    /// KAN-26 tanda 3, so its refusal is expressed only as Skipped + NoTarget. These rows keep a NULL
    /// column: there is no backfill, and inventing one would mean re-deriving a fact from a document
    /// whose shape has since changed.
    /// </summary>
    private const string RealStoredNoTargetTrace =
        """{"_schema":1,"creditGenerated":true,"commission":{"amount":0,"currency":"EUR"},"steps":[{"component":"Trigger","outcome":"Applied"},{"component":"Base","outcome":"Applied","output":{"amount":50000,"currency":"EUR"}},{"component":"Rate","outcome":"Skipped","input":{"amount":50000,"currency":"EUR"},"output":{"amount":0,"currency":"EUR"},"operand":0,"rateTable":"AttainmentBased","attainmentSource":"NoTarget"},{"component":"Modifier","outcome":"NotConfigured","input":{"amount":0,"currency":"EUR"},"output":{"amount":0,"currency":"EUR"}},{"component":"Cap","outcome":"NotConfigured","input":{"amount":0,"currency":"EUR"},"output":{"amount":0,"currency":"EUR"}},{"component":"Floor","outcome":"NotConfigured","input":{"amount":0,"currency":"EUR"},"output":{"amount":0,"currency":"EUR"}}]}""";

    private static RuleCalculationTrace TraceWith(
        RuleCalculationOutcome rateOutcome,
        RateRefusalReason? refusal,
        AttainmentSource? source = null)
        => new()
        {
            CreditGenerated = true,
            Commission = Money.Of(0m, EUR),
            Steps =
            [
                new RuleCalculationStep
                {
                    Component = RuleCalculationComponent.Trigger,
                    Outcome = RuleCalculationOutcome.Applied,
                },
                new RuleCalculationStep
                {
                    Component = RuleCalculationComponent.Rate,
                    Outcome = rateOutcome,
                    Input = Money.Of(50_000m, EUR),
                    Output = Money.Of(0m, EUR),
                    RateTable = RateTableType.AttainmentBased,
                    AttainmentSource = source,
                    RateRefusal = refusal,
                },
                new RuleCalculationStep
                {
                    Component = RuleCalculationComponent.Floor,
                    Outcome = RuleCalculationOutcome.NotConfigured,
                },
            ],
        };

    // ══ The document still reads, and still says what it said ═════════════════════════════════

    [Fact]
    public void The_real_stored_trace_still_deserialises_and_its_rate_step_is_unchanged()
    {
        var trace = CalculationTraceSerializer.Deserialize(RealStoredNoTargetTrace);

        trace.Should().NotBeNull();
        var rate = trace!.Steps.Single(s => s.Component == RuleCalculationComponent.Rate);

        rate.Outcome.Should().Be(RuleCalculationOutcome.Skipped);
        rate.AttainmentSource.Should().Be(AttainmentSource.NoTarget);
        rate.RateRefusal.Should().BeNull(
            "this row predates KAN-26 tanda 3 — its refusal is only Skipped + NoTarget, and that is " +
            "exactly why it gets a NULL column instead of a guessed backfill");
    }

    /// <summary>
    /// ★ THE ENUMS STAY TEXT. A compact form keyed on ordinals would reinterpret every stored trace
    /// the next time a member is inserted. This test fails the moment the encoding changes.
    /// </summary>
    [Fact]
    public void A_refusal_is_written_into_the_document_as_its_name()
    {
        var json = CalculationTraceSerializer.Serialize(
            TraceWith(RuleCalculationOutcome.Skipped, RateRefusalReason.AmountOutsideTable));

        json.Should().Contain("\"rateRefusal\":\"AmountOutsideTable\"");
        json.Should().Contain("\"outcome\":\"Skipped\"");
    }

    /// <summary>A step that priced the sale carries no refusal, so nothing is written for it.</summary>
    [Fact]
    public void An_applied_rate_step_writes_no_refusal_field_at_all()
    {
        var json = CalculationTraceSerializer.Serialize(
            TraceWith(RuleCalculationOutcome.Applied, refusal: null, AttainmentSource.Measured));

        json.Should().NotContain("rateRefusal");
    }

    // ══ The invariant: the column is what the document says ═══════════════════════════════════

    /// <summary>
    /// ★★ THE ANTI-DRIFT TEST, AND IT DELIBERATELY TAKES THE LONG WAY ROUND. The column is computed
    /// from the trace OBJECT; the expectation is parsed back out of the SERIALISED DOCUMENT. Reading
    /// both from the same object would prove only that a field equals itself.
    /// </summary>
    [Theory]
    [InlineData(RateRefusalReason.NoQuotaInEffect)]
    [InlineData(RateRefusalReason.NoMatchingBracket)]
    [InlineData(RateRefusalReason.AmountOutsideTable)]
    public void The_column_says_exactly_what_the_stored_document_says(RateRefusalReason refusal)
    {
        var trace = TraceWith(RuleCalculationOutcome.Skipped, refusal);

        var column = CreditRefusalProjection.FromTrace(trace);
        var json = CalculationTraceSerializer.Serialize(trace);

        var fromDocument = JsonDocument.Parse(json)
            .RootElement.GetProperty("steps")
            .EnumerateArray()
            .Single(s => s.GetProperty("component").GetString() == "Rate")
            .GetProperty("rateRefusal").GetString();

        column.Should().Be(fromDocument);
        column.Should().Be(refusal.ToString());
    }

    [Fact]
    public void A_trace_whose_rate_applied_projects_to_null()
    {
        var trace = TraceWith(RuleCalculationOutcome.Applied, refusal: null, AttainmentSource.Measured);

        CreditRefusalProjection.FromTrace(trace).Should().BeNull();
        CalculationTraceSerializer.Serialize(trace).Should().NotContain("rateRefusal");
    }

    /// <summary>
    /// No trace means no engine run recorded it — a credit built by hand, or one of the 1,296 rows
    /// that predate the column. Null in, null out, and that is a real answer rather than a gap.
    /// </summary>
    [Fact]
    public void No_trace_projects_to_null()
    {
        CreditRefusalProjection.FromTrace(null).Should().BeNull();
    }

    /// <summary>
    /// ★ A TRACE WITH NO RATE STEP IS NOT A CRASH. A trigger that did not match produces a trace
    /// whose only step is the Trigger, and this projection runs over every credit the engine writes.
    /// </summary>
    [Fact]
    public void A_trace_without_a_rate_step_projects_to_null()
    {
        var trace = new RuleCalculationTrace
        {
            CreditGenerated = false,
            Steps = [new RuleCalculationStep
            {
                Component = RuleCalculationComponent.Trigger,
                Outcome = RuleCalculationOutcome.NotMatched,
            }],
        };

        CreditRefusalProjection.FromTrace(trace).Should().BeNull();
    }

    /// <summary>
    /// The old shape projects to null too. ★ IT IS NOT RE-DERIVED FROM Skipped + NoTarget: that pair
    /// is how tanda 2 wrote a refusal before the code existed, and reconstructing it here would make
    /// the projection carry two grammars — the current one and an archaeological one — with the
    /// second silently deciding what old money meant. Those five rows stay null and stay visible as
    /// null.
    /// </summary>
    [Fact]
    public void The_pre_tanda_3_shape_projects_to_null_rather_than_being_reconstructed()
    {
        var trace = CalculationTraceSerializer.Deserialize(RealStoredNoTargetTrace);

        CreditRefusalProjection.FromTrace(trace).Should().BeNull();
    }
}
