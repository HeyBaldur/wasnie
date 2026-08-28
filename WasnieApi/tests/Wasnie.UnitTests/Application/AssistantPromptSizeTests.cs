using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Domain.Assistant;
using Wasnie.Infrastructure.Services.Assistant;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// How big the assistant's system prompt gets, measured on the shape production actually sends.
///
/// ★★ WHAT THIS IS, AND WHAT IT IS NOT. It is a REGRESSION GUARD against the prompt growing without
/// anybody noticing — a rule added here, a manual section there, none of them large on their own. It is
/// NOT a statement that the prompt fits inside the provider's allowance. **That allowance is not
/// established.** The number below was measured against this codebase, not read off a provider's
/// documentation, and it must never be cited as evidence that a request will be accepted.
///
/// ★ THE GUARD IT REPLACES CLAIMED TO BE EXACTLY THAT, AND THAT IS WHY IT FAILED AT ITS JOB. It asserted
/// "&lt; 6,000 tokens" with a comment about a 413 and a tokens-per-minute allowance, and it measured a
/// prompt with ONE routed section, NO navigation map, NO tool data and a single-message history —
/// a request production never sends. Real ones measured 8,094 to 21,636 tokens. So the guard was
/// simultaneously too strict (it failed on an honest corpus edit) and vacuous (it never once measured
/// what actually goes out). A guard presenting itself as something it is not is worse than no guard:
/// people trust it.
///
/// ★ AND IT WAS PASSING AGAINST A STALE CORPUS. The knowledge base reads the guide from the BUILD
/// OUTPUT, which was copied with `PreserveNewest` — so an edit to `docs/` whose timestamp did not beat
/// the existing copy was simply never measured. Fixed in the csproj; noted here because it is the
/// reason a breach went unseen rather than a detail of this file.
/// </summary>
public sealed class AssistantPromptSizeTests(ITestOutputHelper output)
{
    /// <summary>
    /// The ceiling, in tokens, for the biggest prompt production assembles.
    ///
    /// ★★ IT COMES FROM A MEASUREMENT, AND THE MEASUREMENT IS PRINTED BY THE TEST BELOW so the next
    /// person can re-derive it instead of trusting this line. As of 2026-08-26 the worst realistic case
    /// — four routed sections, the navigation map, a tool payload and a full history — measured close
    /// to <see cref="WorstCaseObserved"/> — a figure I got WRONG by 2,000 on the first attempt and then
    /// corrected from what the test printed, which is the whole argument for printing it. The ceiling is
    /// that measurement plus roughly 15%, which is room for the
    /// manual and the rules to grow normally without anybody editing this number, and not so much room
    /// that a runaway addition slips through.
    ///
    /// ★ RAISING IT IS ALLOWED — DELETING THE MEASUREMENT IS NOT. If this has to go up, run the test,
    /// read the figure it prints, and move both numbers together. A ceiling without its measurement
    /// beside it is precisely the artefact this file was written to replace.
    /// </summary>
    private const int Ceiling = 24_000;

    /// <summary>
    /// What the worst realistic case measured when the ceiling above was chosen: 2026-08-26, four
    /// largest sections + navigation map + a balance payload + a full 20-message history.
    /// System prompt 16,849 tok; whole request 20,649 tok.
    /// </summary>
    private const int WorstCaseObserved = 20_650;

    /// <summary>
    /// chars ÷ 4, the same heuristic the previous guard used, so the two numbers are comparable.
    ///
    /// ★ LINE ENDINGS ARE NORMALISED FIRST, and that is not tidiness. The corpus is read from disk, and
    /// a CRLF working copy carries one extra byte per line — about 41 tokens on the two sections a
    /// broad question routes to. That was enough to move this measurement across the old ceiling, so
    /// whether the guard passed depended on how the repository happened to be checked out. A test whose
    /// verdict changes with `core.autocrlf` is a source of false greens.
    /// </summary>
    private static int Tokens(string text) => text.Replace("\r\n", "\n").Length / 4;

    private static IAssistantKnowledgeBaseProbe Knowledge() => new(
        new FileAssistantKnowledgeBase(NullLogger<FileAssistantKnowledgeBase>.Instance));

    /// <summary>Thin wrapper so the sections can be ordered by size without repeating the LINQ.</summary>
    private sealed record IAssistantKnowledgeBaseProbe(FileAssistantKnowledgeBase Base)
    {
        public IReadOnlyList<string> LargestSectionIds(int count) =>
            Base.Sections.OrderByDescending(s => s.Text.Length).Take(count).Select(s => s.Id).ToList();

        public string TextFor(IEnumerable<string> ids) => Base.TextFor(ids);
    }

    private static AssistantMessage Turn(AssistantMessageRole role, int sequence, int length) =>
        AssistantMessage.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), role,
            new string('x', length), sequence, DateTimeOffset.UtcNow);

    /// <summary>
    /// A full history, at the cap.
    ///
    /// ★ THE SIZES ARE A STATED ASSUMPTION, not a measurement — nothing in the repository knows how long
    /// a real answer is. A question of ~120 characters and an answer of ~1,400 is an explanation with a
    /// few bullet points, which is what this assistant mostly writes.
    /// </summary>
    private static List<AssistantMessage> FullHistory() =>
        Enumerable.Range(0, 20)
            .Select(i => Turn(
                i % 2 == 0 ? AssistantMessageRole.User : AssistantMessageRole.Assistant,
                i,
                i % 2 == 0 ? 120 : 1_400))
            .ToList();

    /// <summary>A tool payload of the shape the balance lookup returns — the largest of the four.</summary>
    private const string ToolData =
        """
        {"found":true,"payeeId":"3f2a5b1c-8d4e-4f6a-9b2c-1d3e5f7a9b0c","payeeName":"Rudolph Chipellin",
        "matchedBy":"ResolvedById","period":"all-time","periodStart":null,"periodEnd":null,
        "balances":[{"currency":"EUR","earnedCommissions":104453.24,"alreadyPaidOut":103173.24,
        "awaitingPayment":1280.00,"outstandingDebt":0.00,"clawbackCredit":1280.00,
        "netPendingPayout":1280.00,"interpretation":"EarningsAndNoDebt"}]}
        """;

    [Fact]
    public void THE_ASSEMBLED_PROMPT_STAYS_UNDER_THE_MEASURED_CEILING()
    {
        var knowledge = Knowledge();
        var navigationMap = new FileUiNavigationMap(NullLogger<FileUiNavigationMap>.Instance).PromptBlock;

        // ★ FOUR SECTIONS, BECAUSE THAT IS WHAT THE ROUTER ASKS FOR. Its instructions say "BE GENEROUS:
        // choose 1 to 4 of them" — so four of the largest is the worst case the router can produce, not
        // a pessimistic invention.
        var routed = knowledge.TextFor(knowledge.LargestSectionIds(4));

        var prompt = AssistantPrompt.Build(
            FullHistory(), maxHistory: 20, routed, documentationAvailable: true,
            navigationMap, ToolData);

        var total = prompt.Sum(m => Tokens(m.Content));
        var system = Tokens(prompt.Single(m => m.Role == ChatMessage.SystemRole).Content);

        // ★ PRINTED, ALWAYS. The next person to raise the ceiling should read the number here rather
        // than guessing, and a failure should say how far over it went without anybody re-deriving it.
        output.WriteLine($"worst realistic case: system {system:N0} tok, whole request {total:N0} tok");
        output.WriteLine($"ceiling {Ceiling:N0} tok (measured at ~{WorstCaseObserved:N0} + ~15% headroom)");

        total.Should().BeLessThan(Ceiling,
            "the prompt grew past the measured worst case — re-run this test, read the figure it "
            + "prints, and move Ceiling and WorstCaseObserved together");
    }

    [Fact]
    public void The_measurement_behind_the_ceiling_has_not_drifted_far_from_it()
    {
        // ★ A CEILING DRIFTING AWAY FROM ITS MEASUREMENT IS THE ARTEFACT THIS FILE REPLACED. If the real
        // worst case has fallen to half the ceiling, the guard has stopped guarding anything — and
        // nobody would notice, because a guard that never fires looks exactly like a guard that works.
        var knowledge = Knowledge();
        var navigationMap = new FileUiNavigationMap(NullLogger<FileUiNavigationMap>.Instance).PromptBlock;

        var prompt = AssistantPrompt.Build(
            FullHistory(), maxHistory: 20, knowledge.TextFor(knowledge.LargestSectionIds(4)),
            documentationAvailable: true, navigationMap, ToolData);

        var total = prompt.Sum(m => Tokens(m.Content));

        total.Should().BeGreaterThan(Ceiling / 2,
            "the ceiling is now far above anything real; re-measure and bring it down");
    }

    [Fact]
    public void A_DOCUMENTATION_QUESTION_STILL_SENDS_ONLY_THE_SECTIONS_IT_ASKED_FOR()
    {
        // ★ THE PROPERTY THE OLD GUARD WAS REALLY DEFENDING, kept and stated honestly: routing must not
        // smuggle the whole guide back in. That is about WHAT is sent, not how many tokens it is, so it
        // is asserted as containment rather than as a number.
        var knowledge = Knowledge();
        var routed = knowledge.TextFor(new[] { "s17" });

        var system = AssistantPrompt.BuildSystemMessage(routed, documentationAvailable: true);

        var wholeGuide = Tokens(knowledge.Base.Documentation);
        Tokens(system).Should().BeLessThan(wholeGuide,
            "a routed answer must be smaller than sending everything — that is what routing is for");
    }
}
