using FluentAssertions;
using Wasnie.Application.Assistant.Common;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// THE BELT AGAINST A MODEL THAT STOPS MAKING SENSE.
///
/// ★ WHAT IT IS FOR. Asked to explain a three-rule plan, gpt-oss-20b fell into a repetition loop —
/// "valor mandatorio mandatorio mandatorio…" for hundreds of words — and shipped it to the screen of a
/// product whose subject is what people are paid. The generation model was raised in response; this
/// guard is what makes the promise unconditional, because repetition collapse is a property of language
/// models in general and no prompt removes it.
///
/// ★ THE TWO PROPERTIES THAT MATTER, and they pull against each other: it must CUT a collapse, and it
/// must NEVER cut a legitimate answer. A guard that fires on real prose would replace a rare wall of
/// text with a frequent, inexplicable "try again" — a worse product than the bug.
/// </summary>
public sealed class DegenerationGuardTests
{
    // ── It cuts ───────────────────────────────────────────────────────────────

    [Fact]
    public void The_ACTUAL_collapse_is_cut_off()
    {
        // The shape observed in production, verbatim in structure: a word repeating without end.
        var guard = new DegenerationGuard();
        var cut = false;

        for (var i = 0; i < 200 && !cut; i++)
        {
            cut = guard.Observe("mandatorio ");
        }

        cut.Should().BeTrue("the answer must be cut off, not delivered");
        guard.IsDegenerate.Should().BeTrue();
        guard.Reason.Should().Contain("repeated");
    }

    [Fact]
    public void It_cuts_at_the_LIMIT_and_not_hundreds_of_words_later()
    {
        // ★ WHY THE EXACT POINT MATTERS. Streaming means whatever arrived before the cut is already on
        // screen. The limit is therefore not a formality — it is the bound on how much of the collapse
        // a user ever sees. A dozen, not a thousand.
        var guard = new DegenerationGuard(maxConsecutiveRepeats: 12);
        var observed = 0;

        while (!guard.Observe("mand ") && observed < 500)
        {
            observed++;
        }

        observed.Should().BeLessThan(15, "the cut happens within a couple of words of the limit");
    }

    [Fact]
    public void A_word_split_ACROSS_fragments_is_still_counted_once()
    {
        // ★ THE BUG THIS PREVENTS. The provider splits text at arbitrary byte boundaries, so "mandatorio"
        // routinely arrives as "man" + "dator" + "io ". A guard that counted fragments instead of words
        // would either miss the collapse entirely or fire on ordinary text.
        var guard = new DegenerationGuard(maxConsecutiveRepeats: 5);
        var cut = false;

        for (var i = 0; i < 10 && !cut; i++)
        {
            guard.Observe("man");
            guard.Observe("dator");
            cut = guard.Observe("io ");
        }

        cut.Should().BeTrue("the word is the unit, not the fragment");
    }

    [Fact]
    public void A_runaway_that_never_repeats_a_WORD_is_still_stopped_by_length()
    {
        // The backstop for the collapse that loops over a phrase rather than a word.
        var guard = new DegenerationGuard(maxConsecutiveRepeats: 12, maxCharacters: 1_000);
        var cut = false;

        for (var i = 0; i < 200 && !cut; i++)
        {
            cut = guard.Observe($"palabra{i} distinta{i} cada{i} vez{i} ");
        }

        cut.Should().BeTrue();
        guard.Reason.Should().Contain("characters");
    }

    [Fact]
    public void A_run_that_ends_on_the_very_last_word_is_caught_by_Finish()
    {
        var guard = new DegenerationGuard(maxConsecutiveRepeats: 4);

        // No trailing space on the last one: the final word is only completed by Finish().
        guard.Observe("mand mand mand mand").Should().BeFalse();
        guard.Finish().Should().BeTrue();
    }

    [Fact]
    public void Once_degenerate_it_STAYS_degenerate()
    {
        var guard = new DegenerationGuard(maxConsecutiveRepeats: 3);

        guard.Observe("no no no ").Should().BeTrue();
        guard.Observe("ahora texto perfectamente normal ").Should().BeTrue(
            "a verdict that could be talked out of is not a guard");
    }

    // ── ★ AND IT DOES NOT CUT REAL ANSWERS ────────────────────────────────────

    [Fact]
    public void A_REAL_answer_about_a_three_rule_plan_is_not_cut()
    {
        // ★ THE FALSE-POSITIVE TEST, and it is the one that keeps the guard honest. This is the shape of
        // the answers the assistant actually produces — a markdown table with repeated "no" cells,
        // repeated column headers, repeated units — which is exactly the kind of text a naive detector
        // mangles.
        const string real = """
            **Plan Fiel Tres Reglas** está activo, moneda EUR, del 01-07-2026 al 30-09-2026.

            | Orden | Regla | Trigger | Medición | Tasa | Modificador | Cap | Floor |
            |------|-------|---------|----------|------|-------------|-----|-------|
            | 1 | Comisión Base Revenue | Todas | Revenue | 5 % del importe | Spiff x1,2 | 10.000 EUR | 100 EUR |
            | 2 | Acelerador Hardware Premium | Todas | Revenue | 4 % / 6 % / 8 % por tramo | no | no | no |
            | 3 | Spiff por Volumen de Unidades | Todas | Unidades | 5 EUR por unidad | no | no | no |

            El orden de cálculo es: tabla de tasas, luego modificador, luego cap, luego floor.
            La regla 3 paga 5 EUR por cada unidad vendida, no un porcentaje.
            """;

        var guard = new DegenerationGuard();

        // Fed the way a provider feeds it: in small, arbitrary pieces.
        for (var i = 0; i < real.Length; i += 7)
        {
            guard.Observe(real.Substring(i, Math.Min(7, real.Length - i)))
                .Should().BeFalse("a legitimate answer must never be cut");
        }

        guard.Finish().Should().BeFalse();
        guard.IsDegenerate.Should().BeFalse();
    }

    [Theory]
    [InlineData("No, no, no — eso no es correcto.")]
    [InlineData("| — | — | — | — | — |")]
    [InlineData("0,00 0,00 0,00 EUR en los tres tramos")]
    [InlineData("sí sí sí, es correcto")]
    public void Ordinary_emphasis_and_table_punctuation_do_not_trip_it(string text)
    {
        var guard = new DegenerationGuard();

        guard.Observe(text).Should().BeFalse();
        guard.Finish().Should().BeFalse();
    }

    [Fact]
    public void An_empty_stream_is_not_a_degeneration()
    {
        // Empty answers are a different failure with its own handling in the callers; the guard must not
        // claim them.
        var guard = new DegenerationGuard();

        guard.Observe(null).Should().BeFalse();
        guard.Observe(string.Empty).Should().BeFalse();
        guard.Finish().Should().BeFalse();
    }
}
