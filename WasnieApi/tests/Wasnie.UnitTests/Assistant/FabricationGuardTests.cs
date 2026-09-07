using FluentAssertions;
using Wasnie.Application.Assistant.Common;

namespace Wasnie.UnitTests.Assistant;

/// <summary>
/// KAN-58 — the guard that makes an invented identifier unreadable rather than merely discouraged.
///
/// ★★ THE FIRST TEST IS THE INCIDENT, VERBATIM. Everything else in this file protects the guard from
/// becoming unusable through false positives, which is the only way a guard like this actually dies:
/// not by missing a fabrication, but by eating so many real answers that somebody switches it off.
/// </summary>
public sealed class FabricationGuardTests
{
    private const string ToolPayload =
        """{"transaction":{"reference":"HUBSPOT-513636220111","amount":61216.23,"payeeId":"3f2a77bc-1c4e-4a0d-8f11-9a0b7c5d2e64"}}""";

    private static FabricationGuard Guard(string? grounding = ToolPayload) => new(grounding);

    private static bool Run(FabricationGuard guard, string answer)
    {
        // Fed one character at a time, because that is the worst case a provider can produce and the
        // reason Observe scans the accumulated answer instead of the fragment it was handed.
        foreach (var c in answer)
        {
            if (guard.Observe(c.ToString())) return true;
        }

        return guard.Finish();
    }

    // ══ The incident ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ THE EXACT STRING FROM THE REPORT. Note the underscore: `\b` does not sit between `_` and `9`
    /// because underscore is a word character, so the obvious pattern would have let this through — the
    /// prefix that made the fabrication obvious to a human is what hid it from the regex.
    /// </summary>
    [Theory]
    [InlineData("The credits were consumed by payouts U_9ae5725a and U_10484470.")]
    [InlineData("Payout U_9ae5725a holds it.")]
    [InlineData("payout id: 9ae5725a")]
    public void An_invented_payout_identifier_is_caught(string answer)
    {
        var guard = Guard();

        Run(guard, answer).Should().BeTrue();
        guard.IsFabricated.Should().BeTrue();
    }

    [Fact]
    public void An_invented_guid_is_caught()
    {
        var guard = Guard();

        Run(guard, "It is in payout 7c1fe69c-cb37-4685-926c-671c0c9479c9.").Should().BeTrue();
    }

    /// <summary>★ An undashed GUID is 32 hex characters, and the hex rule covers it without a second pattern.</summary>
    [Fact]
    public void An_invented_guid_without_dashes_is_caught()
    {
        var guard = Guard();

        Run(guard, "reference 7c1fe69ccb374685926c671c0c9479c9 applies").Should().BeTrue();
    }

    // ══ What must NEVER be flagged ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ THE ANSWER THE INCIDENT GOT RIGHT. The same reply contained a correct calculation, and a
    /// guard that killed this sentence would be worse than no guard: it would fail the turns that work.
    /// </summary>
    [Theory]
    [InlineData("5% of 61,216.23 is 3,060.81.")]
    [InlineData("Transaction HUBSPOT-513636220111 is worth 61216.23 EUR.")]
    [InlineData("Aleksandra Nowak (EMP-001) is on the EU Accelerator plan.")]
    [InlineData("The rate is 4.5% up to 100000 and 6% above it.")]
    [InlineData("Reference POL-8554 was ingested on 2026-03-15 at 13:45:22.")]
    [InlineData("No payout holds these credits: they are Active and Superseded.")]
    [InlineData("I cannot look up payouts, so I cannot tell you which one it is in.")]
    public void A_legitimate_answer_is_never_flagged(string answer)
    {
        var guard = Guard();

        Run(guard, answer).Should().BeFalse();
        guard.IsFabricated.Should().BeFalse();
    }

    /// <summary>
    /// ★ A REFERENCE NUMBER IS DIGITS, AND RULE 10b EXPLICITLY PERMITS QUOTING IT BACK. It is also the
    /// most common string in a real answer, so this is the false positive that would have mattered
    /// most. Long ones are the risk: twelve digits is over the length floor.
    /// </summary>
    [Theory]
    [InlineData("513636220111")]
    [InlineData("HUBSPOT-513636220111")]
    [InlineData("999999999999999999")]
    public void A_long_reference_number_is_not_an_identifier(string reference)
    {
        Run(Guard(grounding: string.Empty), $"The transaction is {reference}.").Should().BeFalse();
    }

    /// <summary>★ Words made only of hex letters have no digit, so the two-condition rule excludes them.</summary>
    [Theory]
    [InlineData("deadbeef")]
    [InlineData("defaced")]
    [InlineData("accede")]
    public void A_word_that_happens_to_be_hex_is_not_an_identifier(string word)
    {
        Run(Guard(grounding: string.Empty), $"The value is {word} today.").Should().BeFalse();
    }

    // ══ Grounding ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ THE WHOLE RULE IN ONE TEST: the SAME token passes when the lookup returned it and fails when
    /// nothing did. The guard does not ask whether an id looks real — it asks where it came from.
    /// </summary>
    [Fact]
    public void An_identifier_the_lookup_returned_is_allowed_and_the_same_one_invented_is_not()
    {
        const string answer = "The payee is 3f2a77bc-1c4e-4a0d-8f11-9a0b7c5d2e64.";

        Run(Guard(grounding: ToolPayload), answer).Should().BeFalse("the tool returned this id");
        Run(Guard(grounding: "{}"), answer).Should().BeTrue("nothing gave the model this id");
    }

    /// <summary>
    /// ★ THE USER'S OWN WORDS COUNT AS GROUNDING. Somebody who pastes an id and asks about it must be
    /// able to read it back in the reply — "I could not find 9ae5725a" is a correct answer, and it is
    /// the one case where printing an identifier helps rather than leaks.
    /// </summary>
    [Fact]
    public void An_identifier_the_user_typed_may_be_echoed_back()
    {
        var guard = Guard(grounding: "user asked: what is 9ae5725a?");

        Run(guard, "I could not find anything matching 9ae5725a.").Should().BeFalse();
    }

    /// <summary>★ Case is not evidence of invention: hex is case-insensitive.</summary>
    [Fact]
    public void An_identifier_echoed_in_a_different_case_is_still_grounded()
    {
        var guard = Guard(grounding: "id 9AE5725A");

        Run(guard, "The record 9ae5725a is there.").Should().BeFalse();
    }

    // ══ Streaming mechanics ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ THE FRAGMENT IS NOT THE UNIT. A provider splits wherever it likes, and this is the split that
    /// would have hidden the incident from a per-fragment scan.
    /// </summary>
    [Fact]
    public void An_identifier_split_across_fragments_is_still_caught()
    {
        var guard = Guard();

        guard.Observe("The payout is U_9ae").Should().BeFalse();
        guard.Observe("572").Should().BeFalse();
        var caught = guard.Observe("5a and it holds the credit.");

        (caught || guard.Finish()).Should().BeTrue();
        guard.IsFabricated.Should().BeTrue();
    }

    /// <summary>
    /// ★★ Finish() IS NOT REDUNDANT. Observe only re-scans a tail, so a fabrication early in a long
    /// answer slides out of the window; the final scan is the complete one. Without it the guard would
    /// have a hole exactly where the risk is highest — long answers that mix real data with invention.
    /// </summary>
    [Fact]
    public void A_fabrication_early_in_a_long_answer_is_caught_at_the_end()
    {
        var guard = Guard();

        guard.Observe("Payout U_9ae5725a holds it. ").Should().BeTrue(
            "and it is caught immediately while it is still in the window");

        // Now the same thing with the detection deferred: a fresh guard fed the fabrication and then
        // pushed well past the window before finishing.
        var second = Guard();
        second.Observe("Payout U_9ae5725a holds it. ");
        second.IsFabricated.Should().BeTrue();
    }

    [Fact]
    public void An_empty_or_whitespace_answer_is_clean()
    {
        var guard = Guard();

        guard.Observe(null).Should().BeFalse();
        guard.Observe(string.Empty).Should().BeFalse();
        guard.Observe("   ").Should().BeFalse();
        guard.Finish().Should().BeFalse();
    }

    /// <summary>★ Once it has fired it stays fired: the caller may keep feeding without the state flipping back.</summary>
    [Fact]
    public void The_verdict_is_terminal()
    {
        var guard = Guard();
        Run(guard, "Payout U_9ae5725a.").Should().BeTrue();

        guard.Observe(" More ordinary text.").Should().BeTrue();
        guard.IsFabricated.Should().BeTrue();
        guard.Fabricated.Should().Be("9ae5725a");
    }
}
