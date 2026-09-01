using FluentAssertions;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Infrastructure.Services;

namespace Wasnie.UnitTests.Services;

/// <summary>
/// The one line of the payout PDF that describes how a commission was worked out.
///
/// ★★ A PDF OUTLIVES THE SCREEN. The per-unit display bug was fixed on the payout and credit screens
/// and left here, so a rule paying €5 per unit kept printing "Flat rate: 500%" on the document people
/// forward, file and forward again — against a payout that had already been PAID. The figures beside
/// it were always the server's and were always right; only this sentence was false.
/// </summary>
public sealed class PayoutPdfRateTextTests
{
    private static LineCalculationDto Calc(RateTableDto rateTable) =>
        new(PlanVersion: 3,
            FrozenAt: DateTimeOffset.UtcNow,
            RateTable: rateTable,
            Trigger: new TriggerDto(true, "And", []),
            Modifiers: []);

    // ── The rate: a proportion, or money per unit ─────────────────────────────────────────────

    [Fact]
    public void A_flat_rate_on_the_transaction_amount_is_a_percentage()
    {
        var text = PayoutPdfExportService.FormatRateText(
            Calc(new RateTableDto("Flat", 0.05m, null, null, "TransactionAmount")), "EUR");

        // "5.00%", not "5%": the PDF's `:G` formatting of a decimal keeps its scale. Pre-existing
        // and cosmetic — a unit that is right, written with two decimals.
        text.Should().Contain("5.00%").And.Contain("Plan v3");
    }

    /// <summary>★ THE BUG. `5.00` here is five euros a unit, and it printed as "500%".</summary>
    [Fact]
    public void A_flat_rate_on_the_quantity_is_money_per_unit_and_never_a_percentage()
    {
        var text = PayoutPdfExportService.FormatRateText(
            Calc(new RateTableDto("Flat", 5.00m, null, null, "TransactionQuantity")), "EUR");

        text.Should().Contain("5.00").And.Contain("EUR").And.Contain("per unit");
        text.Should().NotContain("%");
        text.Should().NotContain("500");
    }

    [Fact]
    public void The_per_unit_rate_is_denominated_in_the_payout_line_currency()
        => PayoutPdfExportService.FormatRateText(
                Calc(new RateTableDto("Flat", 5.00m, null, null, "TransactionQuantity")), "USD")
            .Should().Contain("USD");

    /// <summary>
    /// An older payload with no measurement base means the shape almost every rule has. Guessing
    /// "percentage" reproduces a bug we already had; guessing "per unit" would put a currency on a
    /// genuine percentage, which would be a NEW lie.
    /// </summary>
    [Fact]
    public void An_unknown_measurement_base_is_treated_as_a_percentage()
        => PayoutPdfExportService.FormatRateText(
                Calc(new RateTableDto("Flat", 0.05m, null, null, "SomethingNew")), "EUR")
            .Should().Contain("5.00%");

    // ── The tier tables: a count, and deliberately no unit ────────────────────────────────────

    /// <summary>
    /// ★ THE DOCUMENT NAMES NO UNIT FOR A LADDER, ON PURPOSE. It reports how many tiers there are and
    /// stops. The bounds are shown on the payout screen, which knows the reader's locale and currency;
    /// printing them here would mean choosing a unit this method cannot determine — the exact mistake
    /// that produced "0–2000000%".
    /// </summary>
    [Fact]
    public void A_tiered_table_reports_a_count_and_states_no_unit()
    {
        var text = PayoutPdfExportService.FormatRateText(
            Calc(new RateTableDto("Tiered", null,
                [new RateTierDto(0m, 1000m, 0.05m), new RateTierDto(1000m, null, 0.09m)],
                null, "TransactionAmount")), "EUR");

        text.Should().Contain("2 tiers");
        text.Should().NotContain("%");
        text.Should().NotContain("1000");
    }

    [Fact]
    public void An_attainment_table_reports_a_count_and_states_no_unit()
    {
        var text = PayoutPdfExportService.FormatRateText(
            Calc(new RateTableDto("AttainmentBased", null, null,
                [new AttainmentTierDto(0m, 20000m, 0.04m), new AttainmentTierDto(20000m, null, 0.06m)],
                "TransactionAmount")), "EUR");

        text.Should().Contain("2 tiers");
        // The malformed bounds that print as "0–2000000%" elsewhere never appear here at all.
        text.Should().NotContain("2000000");
        text.Should().NotContain("%");
    }

    [Fact]
    public void A_line_with_no_calculation_says_nothing_rather_than_guessing()
        => PayoutPdfExportService.FormatRateText(null, "EUR").Should().BeNull();
}
