using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Domain.Assistant;
using Wasnie.Infrastructure.Services.Assistant;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The two-step answer: a small call that decides WHICH sections, then the generation that uses them.
///
/// ★ WHAT THESE PROVE AND WHAT THEY DO NOT. They pin the mechanics — the router sees only the table of
/// contents, JSON mode is requested, the chosen sections (and only those) reach the generation, and an
/// empty result becomes an honest refusal. Whether the model routes a SYNONYM to the right section is
/// not deterministic and is not asserted: that would be testing somebody else's weights, and would go
/// red on a model upgrade that broke nothing. Rodolfo judges the routing quality on screen.
/// </summary>
public sealed class AssistantRoutingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 16, 0, 0, TimeSpan.Zero);

    /// <summary>Records what it was asked and replies with a scripted routing decision.</summary>
    private sealed class RecordingProvider(string json) : IChatCompletionProvider
    {
        public bool IsConfigured => true;

        public IReadOnlyList<ChatMessage>? RouterMessages { get; private set; }
        public bool JsonModeUsed { get; private set; }

        public Task<string> CompleteJsonAsync(
            IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
        {
            RouterMessages = messages;
            // Reaching this method AT ALL is what proves JSON mode: it is the only entry point that
            // asks the provider for a JSON object, and the router uses no other.
            JsonModeUsed = true;
            return Task.FromResult(json);
        }

        /// <summary>No tools in the routing tests: step 1 is what is under test, not step 1.5.</summary>
        public Task<AssistantToolRequest?> SelectToolAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<AssistantToolSchema> tools,
            CancellationToken cancellationToken) => Task.FromResult<AssistantToolRequest?>(null);

        public async IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return "unused";
        }
    }

    private static IAssistantKnowledgeBase Knowledge() =>
        new FileAssistantKnowledgeBase(NullLogger<FileAssistantKnowledgeBase>.Instance);

    private static AssistantSectionRouter Router(IChatCompletionProvider provider) =>
        new(provider, Knowledge(), NullLogger<AssistantSectionRouter>.Instance);

    private static AssistantMessage UserTurn(string content) =>
        AssistantMessage.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AssistantMessageRole.User, content, 0, Now);

    // ── 1. ★ The router's prompt: the table of contents, NOT the guide ─────────

    [Fact]
    public async Task The_router_sees_only_the_table_of_contents_and_the_question()
    {
        var provider = new RecordingProvider("""{"sections":["s17"]}""");
        var knowledge = Knowledge();

        await Router(provider).RouteAsync("does Wasnie support clawbacks?", CancellationToken.None);

        provider.JsonModeUsed.Should().BeTrue("a chatty reply would break parsing; the format is constrained");

        var system = provider.RouterMessages!.Single(m => m.Role == ChatMessage.SystemRole).Content;

        // It carries the titles…
        system.Should().Contain("s17: 15. Clawback");
        system.Should().Contain(AssistantSectionRouter.RouterInstructions);

        // ★ …and NOT the guide. This is the whole reason the router exists: sending the corpus here
        // would reproduce the 413 that killed the first design, one call earlier.
        system.Should().NotContain("records a **debt** against the payee");
        system.Length.Should().BeLessThan(
            knowledge.Documentation.Length / 5, "the routing call must be a small fraction of the corpus");

        provider.RouterMessages!.Last().Role.Should().Be(ChatMessage.UserRole);
        provider.RouterMessages.Last().Content.Should().Be("does Wasnie support clawbacks?");
    }

    [Fact]
    public void The_table_of_contents_lists_every_section_as_id_and_title()
    {
        var knowledge = Knowledge();

        knowledge.Sections.Should().HaveCountGreaterThan(15);
        knowledge.TableOfContents.Should().Contain("s17: 15. Clawback");
        knowledge.TableOfContents.Should().Contain("s3: 1. The model on one page");

        // Cheap enough to send on every turn — that is what makes two calls affordable.
        (knowledge.TableOfContents.Length / 4).Should().BeLessThan(500);
    }

    // ── 2. ★ Only the routed sections reach the generation ────────────────────

    [Fact]
    public async Task Routed_ids_bring_ONLY_those_sections_into_the_generation_prompt()
    {
        var provider = new RecordingProvider("""{"sections":["s17"]}""");
        var knowledge = Knowledge();

        var ids = await Router(provider).RouteAsync("clawbacks?", CancellationToken.None);
        var routed = knowledge.TextFor(ids);

        var prompt = AssistantPrompt.Build([UserTurn("clawbacks?")], 20, routed, documentationAvailable: true);
        var system = prompt.Single(m => m.Role == ChatMessage.SystemRole).Content;

        // The right section is there…
        system.Should().Contain("## 15. Clawback");
        system.Should().Contain("records a **debt** against the payee");

        // ★ …and the rest of the guide is not.
        system.Should().NotContain("## 6. SplitAtQuota");
        system.Should().NotContain("## 11. Pay runs and payouts");

        // ★ AND IT FITS. The first design sent ~15,300 tokens and every request was refused with 413
        // against a ~12,000-tokens-per-minute allowance. This is the assertion that would have caught it.
        (system.Length / 4).Should().BeLessThan(6_000);
    }

    [Fact]
    public void Sections_are_assembled_in_DOCUMENT_order_whatever_order_the_router_named_them()
    {
        // Handing the model section 15 before section 3 invites it to describe a later rule as if it
        // came first.
        var knowledge = Knowledge();

        var routed = knowledge.TextFor(["s17", "s3"]);

        routed.IndexOf("## 1. The model on one page", StringComparison.Ordinal)
            .Should().BeLessThan(routed.IndexOf("## 15. Clawback", StringComparison.Ordinal));
    }

    [Fact]
    public void An_id_the_router_invented_is_ignored_rather_than_crashing()
    {
        var knowledge = Knowledge();

        var routed = knowledge.TextFor(["s17", "s999", "not-a-number"]);

        routed.Should().Contain("## 15. Clawback");
        routed.Should().NotBeEmpty();
    }

    // ── 2b. ★ THE CALIBRATION BUG ─────────────────────────────────────────────

    [Fact]
    public void The_example_ids_in_the_router_prompt_are_ids_that_ACTUALLY_EXIST()
    {
        // ★ THE TEST THAT WOULD HAVE CAUGHT IT. The prompt showed the model an example of
        // {"sections": ["3", "15"]} — written before the ids gained their `s` prefix, and never
        // updated. The model copied the example faithfully, every returned id missed on an exact
        // match, and every question fell through to "I do not have that in the documentation" — while
        // the routing itself had been CORRECT the whole time. An example that does not match reality
        // teaches the model to be wrong, and nothing else in the system could tell.
        var knowledge = Knowledge();
        var valid = knowledge.Sections.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var exampleIds = System.Text.RegularExpressions.Regex
            .Matches(AssistantSectionRouter.RouterInstructions, "\"(s?[0-9]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();

        exampleIds.Should().NotBeEmpty("the prompt shows the model an example of the format");
        exampleIds.Should().OnlyContain(
            id => valid.Contains(id),
            "every id the prompt demonstrates must be one the knowledge base would actually accept");
    }

    [Fact]
    public void The_router_prompt_contains_the_word_JSON_because_the_API_demands_it()
    {
        // ★ A TRAP FOUND BY PROBING THE REAL API. An OpenAI-compatible endpoint refuses
        // `response_format: json_object` outright unless the messages mention "json" somewhere:
        //
        //   HTTP 400 — "'messages' must contain the word 'json' in some form, to use
        //               'response_format' of type 'json_object'."
        //
        // The current wording says "Return ONLY a JSON object", so it passes. But someone tightening
        // this prompt later could drop that phrase while improving the English, and EVERY routing call
        // would start failing with a 400 that says nothing about prompts. The requirement is invisible
        // in the code, so it is pinned here.
        AssistantSectionRouter.RouterInstructions
            .ToLowerInvariant()
            .Should().Contain("json", "the provider rejects JSON mode unless the prompt mentions it");
    }

    [Fact]
    public void A_bare_id_without_the_prefix_still_resolves()
    {
        // The belt to that pair of braces: a model will occasionally drop the prefix however clearly
        // it is asked not to, and silently dropping the section turns a correct routing decision into
        // "I do not have that in the documentation".
        var knowledge = Knowledge();

        var prefixed = knowledge.TextFor(["s17"]);
        var bare = knowledge.TextFor(["17"]);

        bare.Should().Be(prefixed);
        bare.Should().Contain("## 15. Clawback");

        // …and the surrounding noise a model sometimes adds.
        knowledge.TextFor(["\"17\"", " s17 ", "17."]).Should().Contain("## 15. Clawback");
    }

    [Fact]
    public void The_router_is_instructed_to_be_GENEROUS_not_thrifty()
    {
        // ★ The calibration itself. The first version said "choose between 1 and 3 ids… if NO section
        // could answer, return an empty list", and the model read that as licence to give up: "what is
        // a plan", "show me what Wasnie can do" and "list its sections" all came back empty, and the
        // no-source fallback — built for questions genuinely outside the guide — swallowed questions
        // the guide answers well.
        var rules = AssistantSectionRouter.RouterInstructions;

        rules.Should().Contain("BE GENEROUS");
        rules.Should().Contain("Broad questions deserve broad answers");
        // The distinction that matters: empty means "not about this product", never "I could not pick".
        rules.Should().Contain("empty list ONLY when the question is genuinely not about this product");
        rules.Should().Contain("is NOT a reason to");
        rules.Should().Contain("choosing none is a wrong");
    }

    [Fact]
    public async Task A_broad_product_question_routes_to_sections_and_never_to_the_fallback()
    {
        // The mechanics of the reported failures: given a router that answers with overview sections,
        // the generation must receive their text — not the no-source prompt.
        var provider = new RecordingProvider("""{"sections":["s3","s6"]}""");
        var knowledge = Knowledge();

        var ids = await Router(provider).RouteAsync("what can Wasnie do?", CancellationToken.None);
        var routed = knowledge.TextFor(ids);

        ids.Should().NotBeEmpty();
        routed.Should().Contain("## 1. The model on one page");
        routed.Should().Contain("## 4. Plan and Rules");

        var system = AssistantPrompt.BuildSystemMessage(routed, documentationAvailable: true);
        system.Should().NotBe(AssistantPrompt.NoSourcePrompt);
        system.Should().Contain(AssistantPrompt.DocumentationHeader);

        // ★ Generous, not unbounded: a broad question must not smuggle the whole guide back in — that
        // is what exceeded the per-minute allowance in the first place.
        system.Should().NotContain("## 15. Clawback");
        (system.Length / 4).Should().BeLessThan(6_000);
    }

    // ── 3. ★ THE FALLBACK — an empty route must never mean "answer freely" ────

    [Fact]
    public async Task An_empty_route_produces_the_NO_SOURCE_prompt_and_never_throws()
    {
        // ★ THE RULE THAT COSTS THE MOST IF IT FAILS. With no section the assistant has NO source —
        // and a model with no source does not stay silent, it answers from training, fluently, wearing
        // the product's badge. In a system that decides what people are paid that is the worst possible
        // output. So "no section" is turned into an explicit instruction to say so.
        var provider = new RecordingProvider("""{"sections":[]}""");
        var knowledge = Knowledge();

        var ids = await Router(provider).RouteAsync("how do I make risotto?", CancellationToken.None);

        ids.Should().BeEmpty();

        var routed = knowledge.TextFor(ids);
        routed.Should().BeEmpty();

        var system = AssistantPrompt.BuildSystemMessage(routed, documentationAvailable: true);

        system.Should().Be(AssistantPrompt.NoSourcePrompt);
        system.Should().Contain("contains NOTHING that answers this question");
        system.Should().Contain("must not answer it from general knowledge");
        system.Should().Contain("Do NOT invent a feature");
        // It must not silently become the ordinary confined prompt with an empty corpus.
        system.Should().NotContain(AssistantPrompt.DocumentationHeader);
    }

    [Fact]
    public async Task Malformed_router_output_degrades_to_the_no_source_answer_rather_than_throwing()
    {
        // Structured-output mode makes this very unlikely, not impossible. An unparseable route is a
        // soft miss: "I do not have that in the documentation" is safe and, in that moment, true.
        foreach (var reply in new[] { "not json at all", "", """{"sections":null}""", """{"other":[1]}""" })
        {
            var ids = await Router(new RecordingProvider(reply)).RouteAsync("anything", CancellationToken.None);

            ids.Should().BeEmpty($"'{reply}' cannot be read as a routing decision");
            AssistantPrompt.BuildSystemMessage(Knowledge().TextFor(ids), true)
                .Should().Be(AssistantPrompt.NoSourcePrompt);
        }
    }

    [Fact]
    public void A_missing_guide_is_a_DIFFERENT_silence_from_a_guide_that_does_not_cover_it()
    {
        // Two absences that must not be conflated: no documentation at all (the assistant is
        // unanchored and admits it) versus documentation that simply says nothing on the topic (the
        // assistant says THAT, which is a real answer).
        AssistantPrompt.BuildSystemMessage(string.Empty, documentationAvailable: false)
            .Should().Be(AssistantPrompt.FallbackPrompt);

        AssistantPrompt.BuildSystemMessage(string.Empty, documentationAvailable: true)
            .Should().Be(AssistantPrompt.NoSourcePrompt);
    }

    // ── 4. The confinement from the first attempt is untouched ────────────────

    [Fact]
    public async Task The_confinement_rules_still_ride_with_a_routed_answer()
    {
        var provider = new RecordingProvider("""{"sections":["s17"]}""");
        var ids = await Router(provider).RouteAsync("clawbacks?", CancellationToken.None);

        var system = AssistantPrompt.BuildSystemMessage(Knowledge().TextFor(ids), true);

        system.Should().Contain("ONLY SOURCE OF TRUTH");
        system.Should().Contain("SAY WHEN YOU DO NOT KNOW");
        system.Should().Contain("NEVER invent a feature");
        system.Should().Contain("STAY ON INCENTRA");
        system.Should().Contain("YOU EXPLAIN, YOU DO NOT ACT");
        system.Should().Contain("Remember: answer only from the documentation");
    }

    [Fact]
    public async Task The_whole_turn_stays_under_the_per_minute_allowance()
    {
        // Both calls together, measured against the limit that actually binds. ~500 tokens to route,
        // a few thousand to answer — against ~12,000 a minute, that is several turns, where the first
        // design could not complete one.
        var provider = new RecordingProvider("""{"sections":["s17","s19"]}""");
        var knowledge = Knowledge();

        var ids = await Router(provider).RouteAsync("clawbacks?", CancellationToken.None);

        var routerTokens = provider.RouterMessages!.Sum(m => m.Content.Length) / 4;
        var generationTokens = AssistantPrompt.BuildSystemMessage(knowledge.TextFor(ids), true).Length / 4;

        routerTokens.Should().BeLessThan(1_000);
        (routerTokens + generationTokens).Should().BeLessThan(
            9_000, "the whole turn must leave room inside a ~12,000 token-per-minute allowance");
    }
}
