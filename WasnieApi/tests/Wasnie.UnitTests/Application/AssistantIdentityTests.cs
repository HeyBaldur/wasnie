using FluentAssertions;
using Wasnie.Application.Assistant.Common;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// ★ WHO THE ASSISTANT SAYS IT IS — and, just as much, what it must never claim.
///
/// Asked in Spanish what it was, the assistant answered that it was a language model developed by a
/// well-known outside lab, and said it again when challenged. Nothing in the prompt contradicted it,
/// so the model answered from its training wearing Incentra's badge.
///
/// ★ WHAT THESE CAN AND CANNOT PROVE, the same limit AssistantConfinementTests states: they pin what
/// REACHES the model, which is deterministic. Whether it then obeys is a property of somebody else's
/// weights and is judged on screen. What makes this file worth writing anyway is its second half —
/// the regression tests guard against a FUTURE EDIT rather than against the model, and that is
/// exactly the kind of thing a test can actually hold.
/// </summary>
public sealed class AssistantIdentityTests
{
    private const string SampleDoc = "## 15. Clawback\nIncentra records a debt against the payee.";

    /// <summary>
    /// Every variant a real question can land in. The identity block has to be in all three, and the
    /// least obvious one matters most: an identity question matches no section of the handbook, so the
    /// router hands Build an empty corpus and it falls through to the no-source prompt.
    /// </summary>
    private static IEnumerable<(string Variant, string Prompt)> AllVariants()
    {
        yield return ("confinement", AssistantPrompt.BuildSystemMessage(SampleDoc));
        yield return ("no-source", AssistantPrompt.BuildSystemMessage(string.Empty, documentationAvailable: true));
        yield return ("fallback", AssistantPrompt.BuildSystemMessage(string.Empty, documentationAvailable: false));
        yield return ("identity-block", AssistantPrompt.IdentityRules);
    }

    private static TheoryData<string> Rows(params string[] values)
    {
        var data = new TheoryData<string>();
        foreach (var value in values) data.Add(value);
        return data;
    }

    public static TheoryData<string> PromptVariants() =>
        Rows(AllVariants().Select(v => v.Variant).ToArray());

    private static string PromptFor(string variant) =>
        AllVariants().Single(v => v.Variant == variant).Prompt;

    // ── The block is present, in every variant ────────────────────────────────

    [Theory]
    [MemberData(nameof(PromptVariants))]
    public void Every_prompt_variant_carries_the_identity_block(string variant)
    {
        // ★ NOT TIDINESS. An identity block present only in the confinement rules would read perfectly
        // and never once fire on the question it was written for — that question arrives with no source.
        var prompt = PromptFor(variant);

        prompt.Should().Contain("WHO YOU ARE", $"the {variant} prompt can be asked who it is");
        prompt.Should().Contain("Incentra AI Assistant", $"the {variant} prompt must state the persona");
    }

    [Theory]
    [MemberData(nameof(PromptVariants))]
    public void Every_prompt_variant_carries_the_absolute_prohibitions(string variant)
    {
        var prompt = PromptFor(variant);

        prompt.Should().Contain("claim to be a human being", variant);
        prompt.Should().Contain("deny being an artificial intelligence", variant);
        prompt.Should().Contain("Incentra built, trained or owns", variant);
        prompt.Should().Contain("name any specific outside company", variant);
        prompt.Should().Contain("promise anything about what becomes of it", variant);
    }

    // ── The block outranks rule 3 ─────────────────────────────────────────────

    [Fact]
    public void An_identity_question_is_pulled_out_of_scenario_2A_explicitly()
    {
        // ★ THE ABSENCE WAS NOT THE WHOLE BUG. Rule 3 routes anything not about the product to 2A, and
        // 2A's own wording is "say what you are" — a "say what you are" sitting in the one branch that
        // must not own this question. Sitting quietly beside rule 3 would not have been enough.
        var prompt = PromptFor("confinement");

        prompt.Should().Contain("IS NOT SCENARIO 2A");
        prompt.Should().Contain("3·NOT THIS RULE",
            "rule 3 has to point back, not merely be overridden from a distance");
    }

    [Fact]
    public void The_identity_block_reaches_the_model_before_rule_3_does()
    {
        // Position is part of the mechanism here, so it is asserted rather than assumed.
        var prompt = PromptFor("confinement");

        prompt.IndexOf("WHO YOU ARE", StringComparison.Ordinal)
            .Should().BeLessThan(prompt.IndexOf("3. STAY ON INCENTRA", StringComparison.Ordinal));
    }

    [Fact]
    public void The_no_source_prompt_does_not_treat_an_identity_question_as_off_topic()
    {
        // ★ This is the variant the reported bug went through. No section covers "who are you?", and
        // this prompt's entire job is to say it has no source — but having no source is not the same
        // as having no answer, and here the answer is right above it.
        PromptFor("no-source").Should().Contain("UNLESS IT IS A QUESTION ABOUT YOU");
    }

    [Fact]
    public void The_last_thing_the_model_reads_does_not_pull_identity_back_into_2A()
    {
        // The reminder after the corpus exists because instructions nearest the question carry the most
        // weight — and it restates the 2A/2B/2C branch, which is precisely the pull being corrected.
        PromptFor("confinement").Should().Contain("that is none of 2A/2B/2C");
    }

    [Fact]
    public void Sending_a_data_question_to_the_administrator_is_reconciled_with_the_rule_forbidding_it()
    {
        // The prompt elsewhere forbids "ask your administrator" in ANY language or phrasing, because
        // this user IS the administrator of their environment. Left unreconciled the identity block
        // would contradict it outright, and the model would arbitrate which absolute wins.
        PromptFor("confinement").Should()
            .Contain("The ONE exception is a question about where their data goes");
    }

    // ── ★ Regression: anti-coupling ───────────────────────────────────────────

    /// <summary>Every third-party name a future edit might pin here to be "more transparent".</summary>
    public static TheoryData<string> ThirdPartyNames() => Rows(
        "OpenAI", "ChatGPT", "GPT", "Groq", "OpenRouter", "Anthropic", "Claude",
        "Llama", "Mistral", "Gemini", "DeepSeek", "Qwen");

    [Theory]
    [MemberData(nameof(ThirdPartyNames))]
    public void The_prompt_names_no_third_party_provider_or_model(string name)
    {
        // ★ THIS IS AN ANTI-COUPLING RULE, NOT A LIE-BY-OMISSION ONE. A vendor name written here is a
        // fact that lives somewhere else — configuration, and arrangements no line in this file can
        // see. The arrangement can change; this sentence would not; and on that day the prompt begins
        // telling users something false with complete confidence. It is the same defect as a gradient
        // hard-coding the colour it should have inherited from the background.
        foreach (var (variant, prompt) in AllVariants())
        {
            prompt.Should().NotContainEquivalentOf(name,
                $"the {variant} prompt must not pin a vendor name that configuration owns");
        }
    }

    // ── ★ Regression: anti-assertion ──────────────────────────────────────────

    /// <summary>
    /// The comforting sentences that were drafted twice and cut twice. Each was proposed in good faith
    /// and each is unbacked: the zero-retention setting is recorded in a single place that warns it is
    /// not verifiable from this repository and describes one provider's account while the code ships
    /// two; the signed agreement does not exist and its absence is an open release blocker; and there
    /// is no user-facing privacy policy at all — no route, no page, no document.
    /// </summary>
    public static TheoryData<string> UnbackedReassurances() => Rows(
        "privacy policy", "política de privacidad", "polityka prywatności",
        "data processing agreement", "DPA", "acuerdo de tratamiento",
        "under contract", "contracted", "subprocessor", "subencargado",
        "not used to train", "does not train", "no se usa para entrenar",
        "zero data retention", "zero retention", "never stored");

    [Theory]
    [MemberData(nameof(UnbackedReassurances))]
    public void The_prompt_promises_no_document_contract_or_guarantee(string reassurance)
    {
        // ★ THIS TEST IS THE POINT OF THE FILE. The pull toward warming this answer up is real — the
        // frightened user deserves comfort, and both earlier drafts reached for it. But a prompt is not
        // a compliance mechanism and its rules can be talked around, so the only policy safe to pin
        // here is one the company could tolerate leaking. The honest, unadorned version is. A guarantee
        // that turns out to be unbacked is not: it converts a perception problem into evidence of bad
        // faith, aimed squarely at the person who bought the product.
        //
        // If this goes red, the question is not how to satisfy it. It is whether the thing being added
        // is TRUE and VERIFIABLE — and if it were, it would arrive with the document behind it.
        foreach (var (variant, prompt) in AllVariants())
        {
            prompt.Should().NotContainEquivalentOf(reassurance,
                $"the {variant} prompt must assert nothing it cannot stand behind");
        }
    }

    [Fact]
    public void The_prompt_never_points_the_user_at_the_internal_compliance_board()
    {
        // docs/Legal.md states of itself that it is NOT a privacy policy. Linking it would hand a
        // customer an internal document enumerating this product's own open compliance gaps.
        foreach (var (variant, prompt) in AllVariants())
        {
            prompt.Should().NotContainEquivalentOf("Legal.md", variant);
        }
    }

    // ── The block must not answer questions nobody asked ──────────────────────

    [Theory]
    [MemberData(nameof(PromptVariants))]
    public void THE_BLOCK_IS_BOUNDED_TO_ACTUAL_IDENTITY_QUESTIONS(string variant)
    {
        // ★★ THE REGRESSION THIS FILE'S OWN WORK CAUSED, found in runtime a fortnight later. Asked
        // "¿no entiendes mi pregunta?" — a COMPLAINT that a previous answer had contradicted the
        // screen — the assistant recited this entire block, explained that it was an artificial
        // intelligence using language-processing infrastructure, and never came back to the question
        // it had been asked. The identity work was correct and its trigger was too wide: an answer
        // about what the assistant IS displaced the answer the user was waiting for.
        //
        // The fix is a boundary, not a retreat. Three questions belong here — what you are, who made
        // you, where the data goes — and a complaint is none of them.
        var prompt = PromptFor(variant);

        prompt.Should().Contain("BUT ONLY WHEN THEY ACTUALLY ASKED WHAT YOU ARE", variant);
        prompt.Should().Contain("COMPLAINTS ABOUT AN ANSWER, not questions about you", variant);
        prompt.Should().Contain("NEVER replaces an answer they are waiting for", variant);
        prompt.Should().Contain("Not one word about infrastructure", variant);
    }

    [Theory]
    [MemberData(nameof(PromptVariants))]
    public void The_complaints_that_triggered_the_regression_are_named_verbatim(string variant)
    {
        // ★ THE EXACT PHRASE, because a rule about "frustration" in the abstract is not something a
        // small model can classify. It is given the sentences on both sides of the line instead: the
        // ones that are complaints, and the ones that really are identity questions.
        var prompt = PromptFor(variant);

        prompt.Should().Contain("You do not understand me", variant);
        prompt.Should().Contain("that is not what I asked", variant);
    }

    [Theory]
    [MemberData(nameof(PromptVariants))]
    public void THE_ANSWER_TO_A_REAL_IDENTITY_QUESTION_IS_NOT_WEAKENED(string variant)
    {
        // ★ NARROWING THE TRIGGER MUST NOT NARROW THE ANSWER. The question this block was built for
        // still lands squarely inside it and still gets the full treatment — the bug being fixed is
        // over-firing, and the cure for over-firing must never be under-firing on the original case.
        var prompt = PromptFor(variant);

        // ★ AND THE EXAMPLES NAME NO VENDOR, which is not a detail: the test above forbids this
        // prompt carrying a provider or model name at all, so "are you <some product>?" cannot be
        // written here even as an example of a user's question. The block's own vendor-free phrasing
        // is what covers that case.
        prompt.Should().Contain(
            "WHEN THEY ASK who you are, who made you, whether you are a person", variant);
        prompt.Should().Contain("some other assistant they have heard of", variant);
    }

    [Fact]
    public void The_reminder_draws_the_same_line_where_it_is_read_last()
    {
        // The reminder already routed identity questions away from 2A/2B/2C. It now also routes
        // complaints away from the identity block — same sentence, both directions, at the position
        // that carries the most weight.
        AssistantPrompt.BuildSystemMessage(SampleDoc).Should()
            .Contain("a COMPLAINT that you misunderstood them is NOT that question");
    }
}
