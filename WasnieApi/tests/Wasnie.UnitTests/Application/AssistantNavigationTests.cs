using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Domain.Assistant;
using Wasnie.Infrastructure.Services.Assistant;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The assistant guiding with real routes.
///
/// ★ WHAT THESE PROVE, AND WHAT THEY CANNOT. They pin the MECHANICS: that the map reaches the model,
/// that the no-invented-routes rule is in the prompt, that the map is a separate artefact from the
/// handbook. Whether the model then produces good steps is not assertable here — it is a property of
/// somebody else's weights, and a test claiming it would go red on a model upgrade that broke nothing.
/// The quality of the guidance is judged on screen.
/// </summary>
public sealed class AssistantNavigationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private static AssistantMessage UserTurn(string content) =>
        AssistantMessage.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            AssistantMessageRole.User, content, 0, Now);

    private const string SampleDoc = "## 4. Plan and Rules\nA plan is the foundation of a comp program.";

    private const string SampleMap =
        "/plans/new | New Plan | Create a compensation plan.\n/payees | Payees | The people who earn.";

    // ── Test 1 — ★ the map reaches the model ──────────────────────────────────

    [Fact]
    public void The_system_message_CARRIES_the_navigation_map()
    {
        // ★ Revert the injection — drop navigationMap from BuildSystemMessage and have it build the
        // documentation-only prompt — and this goes red. That is the whole of pillar 1: without the map
        // in the context the model has no routes and will produce URLs that read right and 404.
        var prompt = AssistantPrompt.Build(
            [UserTurn("how do I create a plan?")], 20, SampleDoc,
            documentationAvailable: true, navigationMap: SampleMap);

        var system = prompt.Single(m => m.Role == ChatMessage.SystemRole);

        system.Content.Should().Contain(SampleMap, "the routes ARE the map's contribution");
        system.Content.Should().Contain(AssistantPrompt.NavigationHeader);
        system.Content.Should().Contain(AssistantPrompt.NavigationFooter);

        // Still step 2's prompt: the documentation is there too, and the question is read last.
        system.Content.Should().Contain(SampleDoc);
        prompt[^1].Role.Should().Be(ChatMessage.UserRole);
    }

    [Fact]
    public void The_REAL_map_reaches_the_model_with_the_route_that_creates_a_plan()
    {
        // The fixture above proves the plumbing; this proves the SHIPPED file arrives, which is the part
        // a fixture can never cover — the map is only useful if the curated one is what travels.
        var map = new FileUiNavigationMap(NullLogger<FileUiNavigationMap>.Instance);
        map.IsAvailable.Should().BeTrue("the map ships next to the binary — see Wasnie.Infrastructure.csproj");

        var system = AssistantPrompt.BuildSystemMessage(
            SampleDoc, documentationAvailable: true, navigationMap: map.PromptBlock);

        system.Should().Contain("/plans/new");
        system.Should().Contain("New Plan", "the label comes from en.json, not from a template's i18n key");
    }

    // ── Test 4 — the rule that forbids inventing a route ──────────────────────

    [Fact]
    public void The_no_invented_routes_rule_is_in_the_system_prompt()
    {
        var system = AssistantPrompt.BuildSystemMessage(
            SampleDoc, documentationAvailable: true, navigationMap: SampleMap);

        system.Should().Contain("NEVER INVENT A URL");
        system.Should().Contain(
            "A route that is not in the map does not exist",
            "the prohibition has to be absolute — a plausible-looking route is exactly what a model produces");

        // The usable fallback. Without it the rule is paralysing and the model would rather say nothing.
        system.Should().Contain("WITHOUT a link");

        // Relative, never a full address: an absolute URL leaves the app the user is already inside.
        system.Should().Contain("relative Markdown, starting with a slash");
        system.Should().Contain("Never write a full address with a domain");
    }

    [Fact]
    public void The_step_by_step_format_is_demanded_and_restated_after_the_corpus()
    {
        var system = AssistantPrompt.BuildSystemMessage(
            SampleDoc, documentationAvailable: true, navigationMap: SampleMap);

        system.Should().Contain("NUMBERED LIST");
        system.Should().Contain("in bold");

        // ★ The reminder sits AFTER the documentation, for the same reason the confinement reminder
        // does: thousands of tokens separate a rule placed before the corpus from the user's question,
        // and what the model reads last carries the most weight.
        var ruleIndex = system.IndexOf("NEVER INVENT A URL", StringComparison.Ordinal);
        var corpusIndex = system.IndexOf(SampleDoc, StringComparison.Ordinal);
        var reminderIndex = system.LastIndexOf("Never invent a URL", StringComparison.Ordinal);

        ruleIndex.Should().BeLessThan(corpusIndex);
        reminderIndex.Should().BeGreaterThan(corpusIndex, "the last word must be the rule, not the material");
    }

    // ── The map is a SEPARATE artefact — the handbook stays clean ─────────────

    [Fact]
    public void The_handbook_was_NOT_contaminated_with_routes()
    {
        // ★ THE SEPARATION IS THE DESIGN, so it is asserted rather than trusted. The guide documents
        // business rules and is published to customers; the routes belong to the app's UI and change
        // when the UI is redesigned. If a later change starts writing URLs into the guide, a screen
        // rename begins dirtying a customer document — this is what notices.
        var knowledge = new FileAssistantKnowledgeBase(NullLogger<FileAssistantKnowledgeBase>.Instance);
        knowledge.IsAvailable.Should().BeTrue();

        var map = new FileUiNavigationMap(NullLogger<FileUiNavigationMap>.Instance);

        foreach (var route in map.Routes)
        {
            knowledge.Documentation.Should().NotContain(
                $"({route})",
                "routes live in UINavigationMap.json, never as links inside the handbook");
        }

        knowledge.Documentation.Should().NotContain("](/", "no relative Markdown links belong in the guide");
    }

    // ── The map itself: facts that came from the code, not from a guess ───────

    [Fact]
    public void Every_route_in_the_map_is_absolute_relative_and_free_of_id_placeholders()
    {
        // ★ A ROUTE WITH AN ID PLACEHOLDER IS A BROKEN LINK WEARING A WORKING ONE'S CLOTHES. The
        // assistant has no id to substitute, so `/plans/:planId` would render as a clickable dead end.
        // Those screens belong in `actionsWithoutRoute`, named and not linked.
        var map = new FileUiNavigationMap(NullLogger<FileUiNavigationMap>.Instance);

        map.Routes.Should().NotBeEmpty();

        foreach (var route in map.Routes)
        {
            route.Should().StartWith("/", "links must stay inside the app the user is already in");
            route.Should().NotStartWith("//", "a protocol-relative URL is an EXTERNAL address");
            route.Should().NotContain(":", "a route with an id placeholder cannot be clicked");
            route.Should().NotContain("http", "never a full address with a domain");
        }
    }

    [Fact]
    public void A_missing_map_degrades_to_guiding_without_links_rather_than_to_guessing()
    {
        // The documentation is present, so the assistant still answers — it simply has no routes. The
        // no-invented-routes rule is what makes that safe: no map means no links, not invented ones.
        var system = AssistantPrompt.BuildSystemMessage(
            SampleDoc, documentationAvailable: true, navigationMap: string.Empty);

        system.Should().Contain(SampleDoc);
        system.Should().NotContain(AssistantPrompt.NavigationHeader);
        system.Should().Contain(AssistantPrompt.ConfinementRules);
    }

    [Fact]
    public void The_no_source_reply_gets_NO_navigation_map()
    {
        // ★ NO SOURCE MEANS NO GUIDANCE, LINKS INCLUDED. This prompt is used when the documentation
        // answers nothing and its whole job is to say so. Handing it a list of screens at that moment
        // invites it to fill the silence with navigation — walking the user confidently into a screen
        // for a capability nobody confirmed exists, which is the failure the no-source prompt exists
        // to prevent, arriving by a new door.
        var system = AssistantPrompt.BuildSystemMessage(
            documentation: string.Empty, documentationAvailable: true, navigationMap: SampleMap);

        system.Should().Be(AssistantPrompt.NoSourcePrompt);
        system.Should().NotContain("/plans/new");
    }

    [Fact]
    public void The_router_prompt_never_carries_the_navigation_map()
    {
        // Step 1 chooses sections from the table of contents and nothing else. Its budget is the reason
        // the two-step design exists at all; adding a few hundred tokens of routes to a call that cannot
        // use them would spend it for nothing.
        var map = new FileUiNavigationMap(NullLogger<FileUiNavigationMap>.Instance);

        foreach (var route in map.Routes)
        {
            AssistantSectionRouter.RouterInstructions.Should().NotContain(route);
        }
    }

    [Fact]
    public void The_map_stays_small_enough_to_send_on_every_generating_call()
    {
        // It is FIXED context, not routed like the documentation, so its size is paid on every message.
        // A rough four-characters-per-token gives the number a meaning; the ceiling is generous because
        // the point is to catch the map growing into a second corpus, not to police a curation choice.
        var map = new FileUiNavigationMap(NullLogger<FileUiNavigationMap>.Instance);

        (map.PromptBlock.Length / 4).Should().BeLessThan(
            1000, "the map rides along with every answer — a large one would eat the routing budget");

        // ★ The README is for the humans curating the file and must NOT be sent: instructions addressed
        // to somebody else, paid for on every message.
        map.PromptBlock.Should().NotContain("PENDING RODOLFO");
        map.PromptBlock.Should().NotContain("_README");
    }
}
