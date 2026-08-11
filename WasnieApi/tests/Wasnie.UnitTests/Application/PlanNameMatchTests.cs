using FluentAssertions;
using Wasnie.Application.Assistant.Tools;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// MATCHING A NAME A LANGUAGE MODEL RETYPED.
///
/// ★ THE INCIDENT. The assistant explained "Q3 2026 - Plan Comercial", then two messages later reported
/// that no such plan existed — because when it rewrote the name into its next tool call it used an
/// em-dash. These tests hold the two halves of the fix apart: the comparison must forgive typography,
/// and it must go on refusing everything else.
/// </summary>
public sealed class PlanNameMatchTests
{
    private const string Stored = "Q3 2026 - Plan Comercial";

    // ── The variants the model actually produces ──────────────────────────────

    [Theory]
    [InlineData("Q3 2026 — Plan Comercial", "em dash")]
    [InlineData("Q3 2026 – Plan Comercial", "en dash")]
    [InlineData("Q3 2026 ‑ Plan Comercial", "non-breaking hyphen")]
    [InlineData("Q3 2026 − Plan Comercial", "minus sign")]
    [InlineData("  Q3 2026 - Plan Comercial  ", "padded with spaces")]
    [InlineData("Q3  2026  -  Plan  Comercial", "doubled spaces")]
    [InlineData("q3 2026 - plan comercial", "lower case")]
    [InlineData(" q3 2026 — plan comercial ", "★ the reported failure, all of it at once")]
    // Not decoration: this model has been observed emitting U+202F in its own prose, so the exotic
    // spaces are as real a substitution as the exotic dashes.
    [InlineData("Q3\u00A02026\u202F\u2014\u00A0Plan Comercial", "no-break and narrow no-break spaces")]
    public void A_name_retyped_by_the_model_still_matches_the_stored_one(string requested, string why)
    {
        PlanNameMatch.AreSame(Stored, requested).Should().BeTrue(why);
    }

    [Fact]
    public void Normalisation_runs_on_BOTH_sides_so_a_plan_STORED_with_an_em_dash_also_matches()
    {
        // Normalising only the request would fix the model and break the tenant who pasted a title out
        // of a document. The stored side is not more trustworthy than the typed one — a human typed it.
        PlanNameMatch.AreSame("Q3 2026 — Plan Comercial", "Q3 2026 - Plan Comercial").Should().BeTrue();
        PlanNameMatch.AreSame("Q3 2026 — Plan Comercial", "Q3 2026 – Plan Comercial").Should().BeTrue();
    }

    // ── ★ AND IT IS STILL AN EXACT MATCH ──────────────────────────────────────

    [Theory]
    [InlineData("Q3 2026 - Plan Comercial Enterprise", "a longer name is a different name")]
    [InlineData("Q3", "a fragment is not the name")]
    [InlineData("Plan Comercial", "another fragment is still not the name")]
    [InlineData("Q4 2026 - Plan Comercial", "one word different is a different plan")]
    [InlineData("Q3 2026 - Plan Comercia", "a typo inside a word is not typography")]
    [InlineData("", "nothing matches nothing")]
    [InlineData(null, "nor does null")]
    public void Anything_that_is_not_the_SAME_name_is_refused(string? requested, string why)
    {
        PlanNameMatch.AreSame(Stored, requested).Should().BeFalse(why);
    }

    [Fact]
    public void A_shared_prefix_does_NOT_collapse_two_different_plans_into_one()
    {
        // ★ THE FIX MUST NOT BECOME A GUESS. Explaining the wrong plan's rates is worse than refusing:
        // the user gets a confident answer about money that belongs to another plan.
        PlanNameMatch.AreSame("Q3_Enterprise", "Q3_SMB").Should().BeFalse();
        PlanNameMatch.AreSame("Q3_Enterprise", "Q3").Should().BeFalse();
        PlanNameMatch.AreSame("Q3_SMB", "Q3").Should().BeFalse();
    }

    [Fact]
    public void A_dash_is_folded_but_NOT_deleted_so_it_still_separates_words()
    {
        // "Plan-A" and "PlanA" are different names, and a normaliser that stripped punctuation instead
        // of folding it would merge them.
        PlanNameMatch.AreSame("Plan-A", "PlanA").Should().BeFalse();
        PlanNameMatch.AreSame("Plan-A", "Plan—A").Should().BeTrue();
    }

    // ── The narrowing key: the half of the fix that is easy to miss ───────────

    [Fact]
    public void The_narrowing_key_is_the_part_of_the_name_typography_cannot_change()
    {
        // ★ WHY THIS MATTERS. The SQL filter is `Name.Contains(search)`. Handing it the raw
        // em-dash name returns no rows at all, so the corrected comparison would never see the plan.
        // The key is the longest run with no dash and no space — unaffected by normalisation on either
        // side, so the row is fetched whatever the separators look like.
        PlanNameMatch.NarrowingKey(" q3 2026 — plan comercial ").Should().Be("comercial");
        PlanNameMatch.NarrowingKey("Q3 2026 - Plan Comercial").Should().Be("Comercial");

        // A single-token name narrows on itself.
        PlanNameMatch.NarrowingKey("Q3_Enterprise").Should().Be("Q3_Enterprise");
    }

    [Fact]
    public void A_name_made_only_of_separators_narrows_on_NOTHING_rather_than_on_an_empty_string()
    {
        // Filtering on "" would be a filter that matches every row while claiming to be a filter.
        PlanNameMatch.NarrowingKey("—  – ").Should().BeNull();
        PlanNameMatch.NarrowingKey("   ").Should().BeNull();
        PlanNameMatch.NarrowingKey(null).Should().BeNull();
    }

    [Fact]
    public void The_narrowing_key_of_a_retyped_name_is_the_SAME_as_of_the_stored_one()
    {
        // The property the whole design rests on: whatever the model did to the separators, the key it
        // produces is a key that finds the stored row.
        PlanNameMatch.NarrowingKey(" q3 2026 — plan comercial ")
            .Should().BeEquivalentTo(PlanNameMatch.NarrowingKey(Stored));
    }

    // ── Words dropped out of the name ─────────────────────────────────────────

    [Theory]
    [InlineData("Plan Comercial EMEA (Test Integral)", "★ the measured failure: the quarter prefix dropped")]
    [InlineData("Q3 2026 — Plan Comercial EMEA", "the parenthetical dropped")]
    [InlineData("Plan Comercial EMEA", "both ends dropped")]
    [InlineData("q3  2026 - plan comercial emea (test integral)", "dropped nothing, but retyped")]
    public void A_name_missing_WHOLE_words_from_an_end_is_a_candidate(string requested, string why)
    {
        PlanNameMatch.IsPartialNameOf("Q3 2026 — Plan Comercial EMEA (Test Integral)", requested)
            .Should().BeTrue(why);
    }

    [Fact]
    public void It_is_SYMMETRIC_because_either_side_can_be_the_shorter_one()
    {
        // The model drops words as readily as the user does, so neither side is the authority on how
        // much of the title got typed. Both directions are candidates.
        PlanNameMatch.IsPartialNameOf("Plan Comercial EMEA", "Q3 2026 - Plan Comercial EMEA").Should().BeTrue();
        PlanNameMatch.IsPartialNameOf("Q3 2026 - Plan Comercial EMEA", "Plan Comercial EMEA").Should().BeTrue();
    }

    [Theory]
    [InlineData("Q3_Enterprise", "Q3", "a prefix INSIDE a token is not a word — this is the guess that must not happen")]
    [InlineData("EMEA Overlay", "Overlay EMEA", "the same words in another order are another name")]
    [InlineData("Q3 2026 - Plan Comercial EMEA (Test Integral)", "Q3 Comercial", "non-adjacent words are not a run")]
    [InlineData("Q3 2026 - Plan Comercial EMEA (Test Integral)", "Plan Comercial LATAM", "a word that is not there")]
    [InlineData("Plan Comercial", "", "nothing is not a partial name")]
    public void Anything_that_is_not_a_RUN_of_whole_words_is_NOT_a_candidate(
        string stored, string requested, string why)
    {
        PlanNameMatch.IsPartialNameOf(stored, requested).Should().BeFalse(why);
    }

    [Fact]
    public void Normalize_leaves_an_already_clean_name_untouched()
    {
        PlanNameMatch.Normalize(Stored).Should().Be(Stored);
    }
}
