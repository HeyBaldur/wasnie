using System.Text;
using System.Text.Json;
using Xunit.Abstractions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Domain.Assistant;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// TEMPORARY measurement harness for the token-budget diagnosis. Asserts nothing; prints a table.
/// Delete after the numbers are recorded.
/// </summary>
public sealed class ZZ_TokenBudgetMeasurement(ITestOutputHelper output)
{
    /// <summary>chars/4 — the same heuristic the codebase's own ceiling uses, so numbers compare.</summary>
    private static int T(string s) => s.Length / 4;

    private static string Repo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CLAUDE.md"))) d = d.Parent;
        return d?.FullName ?? "";
    }

    private static string Guide() => File.ReadAllText(Path.Combine(Repo(), "docs", "Wasnie_Configuration_Guide.md"));
    private static string NavMap() => File.ReadAllText(Path.Combine(Repo(), "docs", "UINavigationMap.json"));

    private static List<(string Id, string Title, string Text)> Sections()
    {
        var text = Guide();
        var result = new List<(string, string, string)>();
        var lines = text.Split('\n');
        var title = ""; var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.StartsWith("## "))
            {
                if (title.Length > 0) result.Add((title, title, sb.ToString()));
                title = line[3..].Trim(); sb.Clear();
            }
            sb.AppendLine(line);
        }
        if (title.Length > 0) result.Add((title, title, sb.ToString()));
        return result;
    }

    private static AssistantMessage Msg(AssistantMessageRole role, int seq, string content) =>
        new()
        {
            Id = Guid.NewGuid(), ConversationId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            Role = role, Content = content, Sequence = seq, CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public void Measure()
    {
        var guide = Guide();
        var navMap = NavMap();
        var sections = Sections();

        void Line(string label, string s) => output.WriteLine($"{label,-56} {s.Length,8:N0} ch {T(s),7:N0} tok");
        void Num(string label, int chars) => output.WriteLine($"{label,-56} {chars,8:N0} ch {chars / 4,7:N0} tok");

        output.WriteLine("=========== A. PROMPT CONSTANTS (always-on path) ===========");
        Line("IdentityRules", AssistantPrompt.IdentityRules);
        Line("ConfinementRules (includes IdentityRules)", AssistantPrompt.ConfinementRules);
        Line("  of which IgnoranceRules (2/2A-2D)", AssistantPrompt.IgnoranceRules);
        Line("  of which CapabilityInventory", AssistantPrompt.CapabilityInventory);
        Line("NavigationRules", AssistantPrompt.NavigationRules);

        output.WriteLine("");
        output.WriteLine("=========== B. DATA-PATH CONSTANTS (only when a lookup ran) ===========");
        Line("DataRules (all four token dictionaries)", AssistantPrompt.DataRules);
        Line("  of which PlanRuleTokenRules", AssistantPrompt.PlanRuleTokenRules);
        Line("  of which BalanceTokenRules", AssistantPrompt.BalanceTokenRules);
        Line("  of which PayeePlansTokenRules", AssistantPrompt.PayeePlansTokenRules);
        Line("  of which AmbiguousPayeeRules", AssistantPrompt.AmbiguousPayeeRules);

        output.WriteLine("");
        output.WriteLine("=========== C. CORPUS ===========");
        Line("WHOLE guide (the first design)", guide);
        output.WriteLine($"sections: {sections.Count}");
        var ordered = sections.OrderByDescending(s => s.Text.Length).ToList();
        foreach (var s in ordered.Take(5)) Num($"  largest: {s.Title[..Math.Min(44, s.Title.Length)]}", s.Text.Length);
        Num("  median section", ordered[ordered.Count / 2].Text.Length);
        Num("  smallest section", ordered[^1].Text.Length);
        Line("UINavigationMap.json (whole, every request)", navMap);

        output.WriteLine("");
        output.WriteLine("=========== D. ASSEMBLED SYSTEM PROMPTS ===========");
        var routed3 = string.Join("\n\n", ordered.Take(3).Select(s => s.Text));
        var routed1 = ordered[ordered.Count / 2].Text;
        var routedBig = string.Join("\n\n", ordered.Take(5).Select(s => s.Text));

        Line("NoSourcePrompt (no section matched)",
            AssistantPrompt.BuildSystemMessage(string.Empty, true));
        Line("FallbackPrompt (corpus unreadable)",
            AssistantPrompt.BuildSystemMessage(string.Empty, false));
        Line("Confinement + 1 median section, no nav",
            AssistantPrompt.BuildSystemMessage(routed1, true));
        Line("Confinement + 3 largest sections, no nav",
            AssistantPrompt.BuildSystemMessage(routed3, true));
        Line("Confinement + 1 median section + NAV MAP",
            AssistantPrompt.BuildSystemMessage(routed1, true, navMap));
        Line("Confinement + 3 largest + NAV MAP",
            AssistantPrompt.BuildSystemMessage(routed3, true, navMap));
        Line("Confinement + 5 largest + NAV MAP  (worst routed)",
            AssistantPrompt.BuildSystemMessage(routedBig, true, navMap));

        var toolData = """{"outcome":"PayeePlans","found":true,"payeeId":"3f2a5b1c-8d4e-4f6a-9b2c-1d3e5f7a9b0c","payeeName":"Rudolph Chipellin","payeeEmployeeCode":"CEO-001","matchedBy":"ResolvedById","includedEnded":false,"assignmentCount":1,"totalAssignments":1,"assignments":[{"planId":"7c1e9a2b-4f5d-4a3b-8c6d-2e4f6a8b0c1d","planName":"Q3 2026 - Plan Comercial EMEA","planVersion":1,"assignmentStatus":"Active","effectiveFrom":"2026-07-01","effectiveTo":"2026-09-30"}]}""";
        Line("Confinement + 1 median + NAV + tool data",
            AssistantPrompt.BuildSystemMessage(routed1, true, navMap, toolData));
        Line("Confinement + 3 largest + NAV + tool data",
            AssistantPrompt.BuildSystemMessage(routed3, true, navMap, toolData));
        Line("NO-DOC + tool data (record in hand)",
            AssistantPrompt.BuildSystemMessage(string.Empty, true, navMap, toolData));

        output.WriteLine("");
        output.WriteLine("=========== E. HISTORY (MaxHistoryMessages = 20) ===========");
        // ASSUMPTION, stated: a user question ~120 chars, an assistant answer ~1,400 chars
        // (a plan explanation with a few bullet points). Both are judgement, not measurement.
        foreach (var (q, a) in new[] { (80, 600), (120, 1_400), (200, 3_000) })
        {
            var history = new List<AssistantMessage>();
            for (var i = 0; i < 20; i++)
                history.Add(Msg(i % 2 == 0 ? AssistantMessageRole.User : AssistantMessageRole.Assistant,
                    i, new string('x', i % 2 == 0 ? q : a)));

            var built = AssistantPrompt.Build(history, 20, routed1, true, navMap, string.Empty);
            var system = built[0].Content;
            var rest = built.Skip(1).Sum(m => m.Content.Length);
            output.WriteLine($"  q={q,4} a={a,5}  ->  history {rest,8:N0} ch {rest / 4,7:N0} tok   |   system {system.Length,8:N0} ch {T(system),7:N0} tok");
        }

        output.WriteLine("");
        output.WriteLine("=========== F. TOOL DEFINITIONS (dispatcher call only) ===========");
        Line("AssistantToolRunner.SelectionInstructions", AssistantToolRunner.SelectionInstructions);
        Line("AssistantToolRunner.IdentifierRules", AssistantToolRunner.IdentifierRules);

        output.WriteLine("");
        output.WriteLine("=========== G. ROUTER CALL ===========");
        Line("AssistantSectionRouter.RouterInstructions", AssistantSectionRouter.RouterInstructions);
        var toc = string.Join("\n", sections.Select((s, i) => $"{i + 1}. {s.Title}"));
        Line("table of contents (21 headings)", toc);
    }
}
