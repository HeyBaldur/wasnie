using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Tools;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Assistant;

/// <summary>
/// The simulation tool as the running application exposes it.
///
/// ★★ WHAT THIS COVERS THAT A UNIT TEST CANNOT: whether the tool is actually WIRED UP. Every unit
/// test constructs it directly, so a missing DI registration would leave the whole suite green while
/// the model never learns the tool exists — and the assistant would go back to doing arithmetic in
/// prose, silently, with nothing failing.
///
/// ★ AND WHAT IT DELIBERATELY DOES NOT COVER. Running the tool's body needs a caller identity:
/// `AuthorizationService` resolves the current user's permissions, and a bare DI scope has no request
/// behind it. Rather than build a fake identity that proves the tool works against plumbing that is
/// not the plumbing production uses, the execution path is covered by unit tests, and the same engine
/// is exercised end-to-end over HTTP by SimulateRuleEndpointTests. Stated here so the gap is a choice
/// on the record rather than something nobody noticed.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class SimulatePlanRulesToolTests
{
    private readonly TestDatabaseFixture _fixture;

    public SimulatePlanRulesToolTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void THE_TOOL_IS_REGISTERED_SO_THE_MODEL_IS_TOLD_IT_EXISTS()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var tools = scope.ServiceProvider.GetServices<IAssistantTool>().ToList();

        tools.Select(t => t.Schema.Name).Should().Contain(SimulatePlanRulesTool.ToolName);
    }

    [Fact]
    public void ITS_SCHEMA_IS_VALID_JSON_SCHEMA_SHAPED_JSON()
    {
        // ★ A MALFORMED SCHEMA IS A 400 FOR THE WHOLE REQUEST, not a failed tool call — the provider
        // rejects the message before the model ever runs, so every question the user asks fails with
        // no clue as to which of the five tools broke it.
        using var scope = _fixture.Factory.Services.CreateScope();
        var tool = scope.ServiceProvider.GetServices<IAssistantTool>()
            .Single(t => t.Schema.Name == SimulatePlanRulesTool.ToolName);

        using var doc = JsonDocument.Parse(tool.Schema.ParametersJson);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("object");
        root.GetProperty("properties").EnumerateObject().Select(p => p.Name)
            .Should().Contain(new[] { "planId", "planName", "amount", "quantity", "attainmentPct" });

        // ★ AMOUNT IS THE ONLY REQUIRED ARGUMENT, on purpose: a plan can be named or identified, and
        // demanding both would make the model choose one and fail on the other.
        root.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("amount");
    }

    [Fact]
    public void Every_registered_tool_has_a_distinct_name()
    {
        // Two tools answering to one name is a coin toss the provider resolves, and the loser simply
        // never runs.
        using var scope = _fixture.Factory.Services.CreateScope();
        var names = scope.ServiceProvider.GetServices<IAssistantTool>()
            .Select(t => t.Schema.Name).ToList();

        names.Should().OnlyHaveUniqueItems();
    }
}
