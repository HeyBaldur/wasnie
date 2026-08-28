using System.Text.Json;
using Xunit.Abstractions;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Assistant.Tools;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Plans;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The assistant can ask the engine what a plan would pay, instead of working it out in prose.
///
/// ★★ THE CONVERSATION THAT PRODUCED THIS. Asked what three rules would pay on a 7,850 sale with 5
/// units, the assistant multiplied rule 1 out by hand and got 471 — correct; said rule 2 was
/// impossible "because the per-unit amount is missing" when €5.00 was right there in the payload; and
/// called rule 3's revenue brackets "quota attainment 0 – 20,000 %".
///
/// ★ THE CORRECT ANSWER WAS THE MOST DANGEROUS OF THE THREE. Rule 10d forbids deriving figures the
/// lookup did not return, and it exists for the day the cascade has a cap and a floor in it — at
/// which point prose arithmetic silently stops being right. The model was not bad at multiplying; it
/// had nothing to ask.
/// </summary>
public sealed class SimulatePlanRulesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string EUR = "EUR";

    private readonly ApplicationDbContext _db;
    private readonly SimulatePlanRulesHandler _handler;
    private readonly GetPlanByIdHandler _planByIdHandler;
    private readonly Guid _planId = Guid.NewGuid();

    private readonly ITestOutputHelper _output;

    public SimulatePlanRulesTests(ITestOutputHelper output)
    {
        _output = output;

        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        tenantCtx.IsResolved.Returns(true);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenantCtx, Substitute.For<IPublisher>());

        var auth = Substitute.For<IAuthorizationService>();
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var clock = Substitute.For<IClock>();
        clock.UtcNowOffset.Returns(Now);
        var guid = Substitute.For<IGuidGenerator>();
        guid.NewGuid().Returns(_ => Guid.NewGuid());

        _handler = new SimulatePlanRulesHandler(
            _db, auth,
            new RuleCalculationExplainer(NullLogger<RuleCalculationExplainer>.Instance),
            guid, clock);

        _planByIdHandler = new GetPlanByIdHandler(_db, auth);
    }

    public void Dispose() => _db.Dispose();

    // ── The plan from the conversation: three rules ──────────────────────────

    private Plan SeedThreeRulePlan(string name = "Q3 2026 - Sales")
    {
        var plan = Plan.Create(
            TenantId, name, "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            EUR, "system", _planId, Now, Guid.NewGuid());

        // Rule 1 — the one it multiplied out by hand: 5% with a ×1.2 modifier.
        plan.AddRule("Base commission", 1,
            new Measurement { Type = MeasurementType.Revenue },
            RateTable.Flat(0.05m),
            modifier: new Modifier { Type = ModifierType.Multiplier, Factor = 1.2m });

        // Rule 2 — the one it said it could not do: €5.00 per unit.
        plan.AddRule("Per unit bonus", 2,
            new Measurement { Type = MeasurementType.Units },
            RateTable.Flat(5.00m));

        // Rule 3 — the revenue brackets it described as quota attainment.
        plan.AddRule("Tiered", 3,
            new Measurement { Type = MeasurementType.Revenue },
            RateTable.Tiered(new List<RateTier>
            {
                new() { From = 0m,      To = 10_000m, Rate = 0.02m },
                new() { From = 10_000m, To = null,    Rate = 0.04m },
            }));

        _db.CompensationPlans.Add(plan);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return plan;
    }

    private Task<Result<PlanSimulationDto>> Simulate(
        decimal amount = 7_850m, int quantity = 5,
        Guid? planId = null, string? planName = null,
        decimal? attainment = null, decimal? prior = null, decimal? target = null) =>
        _handler.Handle(
            new SimulatePlanRulesQuery(
                planId ?? (planName is null ? _planId : null), planName,
                amount, quantity, attainment, prior, target),
            CancellationToken.None);

    // ══ ★★ The three rules, one call ══════════════════════════════════════

    [Fact]
    public async Task THE_WHOLE_PLAN_IS_EVALUATED_IN_ONE_CALL()
    {
        // ★ ONE ROUND TRIP, THREE NAMED RESULTS. Three separate calls would hand a language model
        // three loose numbers to keep straight, which is three chances to attach rule 2's figure to
        // rule 3 — and the conversation this fixes did exactly that kind of mixing.
        SeedThreeRulePlan();

        var result = await Simulate();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Rules.Should().HaveCount(3);
        result.Value.Rules.Select(r => r.SortOrder).Should().Equal(1, 2, 3);
        result.Value.Rules.Select(r => r.RuleName)
            .Should().Equal("Base commission", "Per unit bonus", "Tiered");
    }

    [Fact]
    public async Task RULE_2_PAYS_TWENTY_FIVE_EUROS_THE_ANSWER_IT_SAID_IT_COULD_NOT_GIVE()
    {
        // ★★ €5.00 × 5 units = €25.00 — the same figure the rule screen's simulator returns, because
        // it is the same engine. The assistant used to say the per-unit amount was missing; it was in
        // the payload all along, and what was missing was anything able to multiply it.
        SeedThreeRulePlan();

        var rule2 = (await Simulate()).Value!.Rules.Single(r => r.SortOrder == 2);

        rule2.Simulated.Should().BeTrue();
        rule2.CommissionAmount.Should().Be(25.00m);
    }

    [Fact]
    public async Task Rule_1_matches_what_the_engine_computes_for_the_same_transaction()
    {
        // 5% of 7,850 = 392.50 → ×1.2 = 471.00. The prose arithmetic happened to land here too; the
        // point is that this number now comes from the cascade rather than from a sentence.
        SeedThreeRulePlan();

        var rule1 = (await Simulate()).Value!.Rules.Single(r => r.SortOrder == 1);

        rule1.CommissionAmount.Should().Be(471.00m);
        rule1.Steps.Select(s => s.Component).Should().Equal(
            RuleCalculationComponent.Trigger, RuleCalculationComponent.Base,
            RuleCalculationComponent.Rate, RuleCalculationComponent.Modifier,
            RuleCalculationComponent.Cap, RuleCalculationComponent.Floor);
    }

    [Fact]
    public async Task Rule_3_reports_the_revenue_brackets_it_actually_walked()
    {
        // The brackets it described as "quota attainment 0 – 20,000 %" are absolute revenue bands of
        // this very transaction. The steps carry the portions, so the model has no reason to guess.
        SeedThreeRulePlan();

        var rule3 = (await Simulate()).Value!.Rules.Single(r => r.SortOrder == 3);

        // 10,000 @2% = 200 · the remaining 5,850* @4% ... 7,850 total → 10,000 not reached.
        rule3.CommissionAmount.Should().Be(157.00m, "7,850 all falls in the first bracket at 2%");
        var rate = rule3.Steps.Single(s => s.Component == RuleCalculationComponent.Rate);
        rate.Tiers.Should().ContainSingle();
    }

    // ══ ★★ Attainment: a code, not a number ═══════════════════════════════

    [Fact]
    public async Task AN_ATTAINMENT_RULE_RETURNS_A_CODE_INSTEAD_OF_A_FIGURE()
    {
        // ★★ THE REFUSAL THE WHOLE FEATURE TURNS ON. The engine defaults attainment to 1.0, so
        // answering anyway reports a rep at full quota as if it were anybody — a figure that looks
        // completely reasonable and is false for almost everyone.
        var plan = Plan.Create(
            TenantId, "Attainment plan", "d",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            EUR, "system", _planId, Now, Guid.NewGuid());

        plan.AddRule("Attainment", 1,
            new Measurement { Type = MeasurementType.Revenue },
            new RateTable
            {
                Type = RateTableType.AttainmentBased,
                AttainmentTiers = new List<AttainmentTier>
                {
                    new() { AttainmentFrom = 0m,    AttainmentTo = 1.00m, Rate = 0.02m },
                    new() { AttainmentFrom = 1.00m, AttainmentTo = null,  Rate = 0.08m },
                },
            });

        _db.CompensationPlans.Add(plan);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var rule = (await Simulate(amount: 10_000m)).Value!.Rules.Single();

        rule.Simulated.Should().BeFalse();
        rule.Blocker.Should().Be(RuleSimulationBlocker.AttainmentContextRequired);
        rule.CommissionAmount.Should().BeNull();
        rule.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task A_SUPPLIED_ATTAINMENT_TRAVELS_MARKED_ALL_THE_WAY_OUT()
    {
        // ★ "With the 100% we assumed" and "with your attainment" are different statements. The
        // provenance has to survive as far as the answer or the distinction is lost exactly where it
        // matters.
        var plan = Plan.Create(
            TenantId, "Attainment plan", "d",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            EUR, "system", _planId, Now, Guid.NewGuid());

        plan.AddRule("Attainment", 1,
            new Measurement { Type = MeasurementType.Revenue },
            new RateTable
            {
                Type = RateTableType.AttainmentBased,
                AttainmentTiers = new List<AttainmentTier>
                {
                    new() { AttainmentFrom = 0m, AttainmentTo = null, Rate = 0.02m },
                },
            });

        _db.CompensationPlans.Add(plan);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var rule = (await Simulate(amount: 10_000m, attainment: 0.4m)).Value!.Rules.Single();

        rule.Simulated.Should().BeTrue();
        rule.Steps.Single(s => s.Component == RuleCalculationComponent.Rate)
            .AttainmentSource.Should().Be(AttainmentSource.Supplied);
    }

    // ══ ★ No writes ═══════════════════════════════════════════════════════

    [Fact]
    public async Task SIMULATING_WRITES_NOTHING()
    {
        SeedThreeRulePlan();

        var before = (_db.Credits.Count(), _db.CompensationTransactions.Count());

        await Simulate();
        await Simulate(amount: 1m, quantity: 1);

        (_db.Credits.Count(), _db.CompensationTransactions.Count()).Should().Be(before);
        _db.ChangeTracker.HasChanges().Should().BeFalse();
    }

    // ══ ★ Tenant scoping ══════════════════════════════════════════════════

    [Fact]
    public async Task A_PLAN_FROM_ANOTHER_TENANT_IS_NOT_FOUND_BY_ID_OR_BY_NAME()
    {
        var foreignId = Guid.NewGuid();
        var foreign = Plan.Create(
            Guid.NewGuid(), "Someone else's plan", "d",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            EUR, "system", foreignId, Now, Guid.NewGuid());
        _db.CompensationPlans.Add(foreign);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        (await Simulate(planId: foreignId)).IsSuccess.Should().BeFalse();
        (await Simulate(planName: "Someone else's plan")).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task The_plan_can_be_resolved_by_name_the_way_the_other_tools_resolve_it()
    {
        SeedThreeRulePlan("Q3 2026 - Sales");

        // An em-dash where a hyphen was stored: the same substitution the other plan tools fold.
        var result = await Simulate(planName: "Q3 2026 — Sales");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Rules.Should().HaveCount(3);
    }

    // ══ ★ The tool's own payload ══════════════════════════════════════════

    [Fact]
    public async Task THE_TOOL_PAYLOAD_CARRIES_THE_FIGURES_AND_NEVER_A_TOTAL()
    {
        // ★ A SUM OF RULES IS NOT A PAYOUT — it ignores which plan applies, quota context and
        // clawback. Printed beside the rules it would be the number people quote.
        SeedThreeRulePlan();

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<SimulatePlanRulesQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci => _handler.Handle((SimulatePlanRulesQuery)ci[0], CancellationToken.None));

        var tool = new SimulatePlanRulesTool(sender, NullLogger<SimulatePlanRulesTool>.Instance);

        var json = await tool.RunAsync(
            $$"""{"planId":"{{_planId}}","amount":7850,"quantity":5}""", CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("found").GetBoolean().Should().BeTrue();
        root.GetProperty("rules").GetArrayLength().Should().Be(3);
        root.TryGetProperty("total", out _).Should().BeFalse("a sum of rules is not a payout");

        var rule2 = root.GetProperty("rules").EnumerateArray()
            .Single(r => r.GetProperty("sortOrder").GetInt32() == 2);
        rule2.GetProperty("commission").GetDecimal().Should().Be(25.00m);
    }

    [Fact]
    public async Task The_tool_refuses_without_naming_the_reason_when_the_plan_is_not_visible()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<SimulatePlanRulesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PlanSimulationDto>.Failure("Plan not found."));

        var tool = new SimulatePlanRulesTool(sender, NullLogger<SimulatePlanRulesTool>.Instance);

        var json = await tool.RunAsync(
            """{"planName":"Whatever","amount":100}""", CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("found").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("outcome").GetString().Should().Be("NotFoundOrNotVisible");
    }

    // ══ ★★ What this payload COSTS ══════════════════════════════════════

    [Fact]
    public async Task THE_PAYLOAD_IS_MEASURED_BECAUSE_IT_IS_THE_BIGGEST_ONE_ANY_TOOL_RETURNS()
    {
        // ★★ AND THE PROMPT GUARD DOES NOT KNOW THAT. AssistantPromptSizeTests models its worst case
        // with a BALANCE payload — a few hundred characters. This one carries, per rule, a full
        // cascade trace AND the rule's whole configuration, times however many rules the plan has.
        // A three-rule plan is a modest plan.
        //
        // ★ PRINTED, ALWAYS, for the same reason the prompt guard prints its own figure: the next
        // person to weigh a change to this payload should read the number rather than guess it.
        SeedThreeRulePlan();

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<SimulatePlanRulesQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci => _handler.Handle((SimulatePlanRulesQuery)ci[0], CancellationToken.None));
        // The tool also reads the configuration; an unstubbed sender returns null and the tool
        // degrades to figures-only, which would make this measurement smaller than reality.
        sender.Send(Arg.Any<GetPlanByIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci => _planByIdHandler.Handle((GetPlanByIdQuery)ci[0], CancellationToken.None));

        var tool = new SimulatePlanRulesTool(sender, NullLogger<SimulatePlanRulesTool>.Instance);
        var json = await tool.RunAsync(
            $$"""{"planId":"{{_planId}}","amount":7850,"quantity":5}""", CancellationToken.None);

        // chars ÷ 4 — the same heuristic AssistantPromptSizeTests uses, so the numbers compare.
        var tokens = json.Length / 4;
        _output.WriteLine($"simulate_plan_rules payload, 3 rules: {tokens:N0} tok ({json.Length:N0} chars)");

        // A ceiling with room to grow, not a target. If a plan of a realistic size blows through this,
        // the prompt budget needs re-deriving before the payload does.
        tokens.Should().BeLessThan(3_000,
            "this payload rides inside the prompt whose ceiling is 24,000 tokens");
    }

    [Fact]
    public async Task The_tool_refuses_malformed_arguments_instead_of_throwing()
    {
        var tool = new SimulatePlanRulesTool(
            Substitute.For<ISender>(), NullLogger<SimulatePlanRulesTool>.Instance);

        foreach (var args in new[] { "not json", "{}", """{"planName":"X"}""" })
        {
            var json = await tool.RunAsync(args, CancellationToken.None);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("found").GetBoolean().Should().BeFalse();
        }
    }
}
