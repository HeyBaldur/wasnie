using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.PlanRules;

/// <summary>
/// The simulation endpoint, end to end.
///
/// ★★ THE POINT OF EXERCISING IT HERE RATHER THAN ONLY IN UNIT TESTS is that two of its guarantees
/// live outside the handler: the tenant boundary is a global query filter applied by EF, and "it
/// writes nothing" is only really provable against a real database.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class SimulateRuleEndpointTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    public SimulateRuleEndpointTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetCompensationDataAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Auth ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Simulate_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/plans/{Guid.NewGuid()}/rules/simulate", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── ★ The rule from the screenshot ───────────────────────────────────────

    [Fact]
    public async Task THE_FLOOR_WINS_OVER_THE_CAP_END_TO_END()
    {
        // ★ 5% of 1,200 = 60 → ×1.2 = 72 → cap 10,000 does not bite → floor 100 lifts it to 100.
        var plan = await CreatePlanAsync(_clientA);

        var body = await SimulateAsync(_clientA, plan.Id, Request(plan.Id, amount: 1200m));

        body.Simulated.Should().BeTrue();
        body.CommissionAmount.Should().Be(100m);
        body.Currency.Should().Be("EUR");

        body.Steps.Select(s => s.Component)
            .Should().Equal("Trigger", "Base", "Rate", "Modifier", "Cap", "Floor");

        body.Steps.Single(s => s.Component == "Cap").Outcome
            .Should().Be("AppliedWithoutEffect", "the cap never bit");
        body.Steps.Single(s => s.Component == "Floor").Outcome
            .Should().Be("Applied", "the floor did");
        body.Steps.Single(s => s.Component == "Floor").OutputAmount.Should().Be(100m);
    }

    [Fact]
    public async Task A_rule_that_has_never_been_saved_can_be_simulated()
    {
        // ★ THE WHOLE REASON THE ENDPOINT TAKES A DEFINITION. The plan below has no rules at all —
        // this definition exists only in the request body, exactly as it does while somebody is
        // still typing it into the form.
        var plan = await CreatePlanAsync(_clientA);

        var body = await SimulateAsync(_clientA, plan.Id, Request(plan.Id, amount: 1200m));

        body.Simulated.Should().BeTrue();
        body.CommissionAmount.Should().Be(100m);
    }

    // ── ★★ Nothing is written ────────────────────────────────────────────────

    [Fact]
    public async Task SIMULATING_CREATES_NOTHING()
    {
        // ★★ A PREVIEW THAT CAN MOVE MONEY IS NOT A PREVIEW. Counted against the real database.
        var plan = await CreatePlanAsync(_clientA);

        var creditsBefore = await CountAsync("Credits");
        var txBefore = await CountAsync("CompensationTransactions");
        var plansBefore = await CountAsync("Plans");
        var rulesBefore = await CountAsync("PlanRules");

        for (var i = 0; i < 3; i++)
        {
            await SimulateAsync(_clientA, plan.Id, Request(plan.Id, amount: 1000m * (i + 1)));
        }

        (await CountAsync("Credits")).Should().Be(creditsBefore);
        (await CountAsync("CompensationTransactions")).Should().Be(txBefore);
        (await CountAsync("Plans")).Should().Be(plansBefore,
            "the scratch plan built to run the engine is an in-memory object, not a row");
        (await CountAsync("PlanRules")).Should().Be(rulesBefore);
    }

    // ── ★ Tenant scoping ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_PLAN_FROM_ANOTHER_TENANT_CANNOT_BE_SIMULATED_AGAINST()
    {
        // ★ Tenant B holds the plan; tenant A asks about it by id and is answered as though it did
        // not exist — which, through the global query filter, it does not.
        var plan = await CreatePlanAsync(_clientB);

        var response = await _clientA.PostAsJsonAsync(
            $"/api/plans/{plan.Id}/rules/simulate", Request(plan.Id, amount: 1200m));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("not found");
    }

    // ── ★★ Attainment is refused, not guessed ────────────────────────────────

    [Fact]
    public async Task AN_ATTAINMENT_RULE_WITHOUT_CONTEXT_IS_REFUSED_RATHER_THAN_ANSWERED()
    {
        // ★★ The engine's default attainment is 1.0, so answering anyway would report the commission
        // of a rep at full quota and present it as anybody's — a figure that looks perfectly
        // reasonable and is false for almost everyone.
        var plan = await CreatePlanAsync(_clientA);

        var request = new
        {
            planId = plan.Id,
            name = "Attainment rule",
            sortOrder = 1,
            measurement = new { _schema = 1, type = 0, sourceField = "amount", aggregation = 0 },
            rateTable = new
            {
                _schema = 1,
                type = 2,
                splitAtQuota = false,
                attainmentTiers = new[]
                {
                    new { attainmentFrom = 0.0, attainmentTo = (double?)1.0, rate = 0.02 },
                    new { attainmentFrom = 1.0, attainmentTo = (double?)null, rate = 0.08 },
                },
            },
            trigger = (object?)null,
            modifier = (object?)null,
            cap = (object?)null,
            floor = (object?)null,
            amount = 10_000m,
            quantity = 1,
        };

        var body = await SimulateAsync(_clientA, plan.Id, request);

        body.Simulated.Should().BeFalse();
        body.Blocker.Should().Be("AttainmentContextRequired");
        body.CommissionAmount.Should().BeNull();
        body.Steps.Should().BeEmpty();
    }

    // ── ★ The save-time rules apply here too ─────────────────────────────────

    [Fact]
    public async Task A_CAP_SCOPE_THE_SYSTEM_REFUSES_TO_SAVE_IS_REFUSED_HERE_TOO()
    {
        var plan = await CreatePlanAsync(_clientA);

        var request = new
        {
            planId = plan.Id,
            name = "Bad cap",
            sortOrder = 1,
            measurement = new { _schema = 1, type = 0, sourceField = "amount", aggregation = 0 },
            rateTable = new { _schema = 1, type = 0, flatRate = 0.05 },
            trigger = (object?)null,
            modifier = (object?)null,
            cap = new { _schema = 1, amount = new { amount = 100m, currency = "EUR" }, scope = "PerPeriod" },
            floor = (object?)null,
            amount = 1200m,
            quantity = 1,
        };

        var response = await _clientA.PostAsJsonAsync(
            $"/api/plans/{plan.Id}/rules/simulate", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Route_and_body_plan_ids_must_agree()
    {
        var plan = await CreatePlanAsync(_clientA);

        var response = await _clientA.PostAsJsonAsync(
            $"/api/plans/{plan.Id}/rules/simulate", Request(Guid.NewGuid(), amount: 1200m));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<SimulationResponse> SimulateAsync(HttpClient client, Guid planId, object request)
    {
        var response = await client.PostAsJsonAsync($"/api/plans/{planId}/rules/simulate", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SimulationResponse>())!;
    }

    /// <summary>
    /// Counts rows with raw SQL rather than through the DbSet.
    ///
    /// ★ NOT A SHORTCUT — the tenant-filtered DbSets cannot even be READ outside a request: the
    /// background-job tenant context throws until SetTenant is called. Raw SQL also counts EVERY
    /// tenant's rows, which is what this assertion wants: "the simulation created nothing anywhere".
    /// </summary>
    private async Task<int> CountAsync(string table)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Database
            .SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM [{table}]")
            .ToListAsync())[0];
    }

    private async Task<PlanResponse> CreatePlanAsync(HttpClient client)
    {
        var request = new
        {
            name = $"Sim Plan {Guid.NewGuid().ToString("N")[..8]}",
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR",
        };

        var response = await client.PostAsJsonAsync("/api/plans", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlanResponse>())!;
    }

    /// Flat 5% + modifier ×1.2 + cap 10,000 + floor 100 — the rule from the screenshot.
    private static object Request(Guid planId, decimal amount, int quantity = 1) => new
    {
        planId,
        name = "Simulated rule",
        sortOrder = 1,
        measurement = new { _schema = 1, type = 0, sourceField = "amount", aggregation = 0 },
        rateTable = new { _schema = 1, type = 0, flatRate = 0.05 },
        trigger = (object?)null,
        modifier = new { _schema = 1, id = Guid.NewGuid(), name = "Boost", type = 0, factor = 1.2, trigger = (object?)null },
        cap = new { _schema = 1, amount = new { amount = 10_000m, currency = "EUR" }, scope = "PerTransaction" },
        floor = new { _schema = 1, amount = new { amount = 100m, currency = "EUR" } },
        amount,
        quantity,
    };

    private sealed record PlanResponse(Guid Id, string Name, string Status, int Version);

    // ★ STRINGS, NOT NUMBERS. Program.cs registers a JsonStringEnumConverter, so the wire carries
    // "Cap" and "AppliedWithoutEffect". Typing these as ints is what this test caught in the
    // frontend model, where numeric enums would have compared false against every value forever.
    private sealed record SimulationResponse(
        bool Simulated,
        string Blocker,
        bool CreditGenerated,
        decimal? CommissionAmount,
        string Currency,
        IReadOnlyList<StepResponse> Steps);

    private sealed record StepResponse(
        string Component,
        string Outcome,
        decimal? InputAmount,
        decimal? OutputAmount);
}
