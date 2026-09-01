using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Assistant.Tools;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Handlers.Plans;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// THE THREE-RULE PLAN FROM THE INCIDENT, ASSERTED FIELD BY FIELD.
///
/// ★ WHAT THIS SETTLES, AND WHY IT IS ITS OWN FILE. The assistant explained two of a plan's three rules
/// and said the third one's "configuration is not available". Two very different faults produce that
/// sentence: a payload that dropped the rule, or a model that gave up on it. This test pins the payload
/// half — the exact plan, the exact three rules, copied from the rows in the tenant where it happened —
/// so the question never has to be reopened by reasoning. The rule DOES travel, complete. What failed
/// was the generation, and no backend test can hold a model to its word.
///
/// It stays after the incident because the Units rule is the one nobody exercises: it is the only
/// measurement that changes the base, and it was the one that went missing.
/// </summary>
public sealed class PlanRulesPayloadCompletenessTests
{
    private sealed class AllowAll : IAuthorizationService
    {
        public Task RequireAsync(string permission, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class HandlerSender(IApplicationDbContext db, IAuthorizationService auth) : ISender
    {
        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default) => request switch
        {
            ListPlansQuery q => (TResponse)(object)await new ListPlansHandler(db, auth).Handle(q, cancellationToken),
            GetPlanByIdQuery q => (TResponse)(object)await new GetPlanByIdHandler(db, auth).Handle(q, cancellationToken),
            _ => throw new NotSupportedException(request.GetType().Name),
        };

        public Task<object?> Send(object r, CancellationToken c = default) => throw new NotSupportedException();
        public Task Send<T>(T r, CancellationToken c = default) where T : IRequest => throw new NotSupportedException();
        public IAsyncEnumerable<TR> CreateStream<TR>(IStreamRequest<TR> r, CancellationToken c = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object r, CancellationToken c = default) => throw new NotSupportedException();
    }

    /// <summary>The plan exactly as it is stored in the tenant that reported the incident.</summary>
    private const string PlanName = "Q3 2026 — Plan Comercial EMEA (Test Integral)";

    // The stray quotation marks are not a typo here — they are in the stored names, typed by an
    // administrator, and they are part of what the model had to read.
    private const string UnitsRuleName = "\"Spiff por Volumen de Unidades\" (Flat sobre Units)";

    private static async Task<JsonElement> RunAsync()
    {
        var tenant = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenant);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(PlanRulesPayloadCompletenessTests)}.{tenant}").Options,
            tenantCtx, Substitute.For<IPublisher>());

        var plan = CompensationPlan.Create(
            tenant, PlanName, "incident repro",
            DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30)),
            "EUR", "seed", Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());

        plan.AddRule(
            "Comisión Base Revenue\" (Flat + Modifier + Cap + Floor)", 1,
            new Measurement { Type = MeasurementType.Revenue, SourceField = "amount" },
            RateTable.Flat(0.05m),
            modifier: new Modifier { Name = "Boost Q3", Type = ModifierType.Spiff, Factor = 1.2m },
            cap: new Cap { Amount = Money.Of(10000m, "EUR"), Scope = CapScope.PerTransaction },
            floor: new Floor { Amount = Money.Of(100m, "EUR") });

        plan.AddRule(
            "\"Acelerador Hardware Premium\" (Attainment + Trigger por categoría)", 2,
            new Measurement { Type = MeasurementType.Revenue, SourceField = "amount" },
            RateTable.AttainmentBased(
            [
                // Ratios of quota, last tier open. The fixture used to carry this plan's REAL
                // boundaries (0-20000, 20000-50000, 50000-100000), which are currency amounts in a
                // ladder the engine indexes by ratio — the exact defect the factories now reject.
                new AttainmentTier { AttainmentFrom = 0m, AttainmentTo = 0.5m, Rate = 0.04m },
                new AttainmentTier { AttainmentFrom = 0.5m, AttainmentTo = 1m, Rate = 0.06m },
                new AttainmentTier { AttainmentFrom = 1m, AttainmentTo = null, Rate = 0.08m },
            ]));

        plan.AddRule(
            UnitsRuleName, 3,
            new Measurement { Type = MeasurementType.Units, SourceField = "amount" },
            RateTable.Flat(5m));

        plan.Activate("seed", DateTimeOffset.UtcNow, Guid.NewGuid());
        db.CompensationPlans.Add(plan);
        db.SaveChanges();

        var tool = new GetPlanRulesTool(
            new HandlerSender(db, new AllowAll()), NullLogger<GetPlanRulesTool>.Instance);

        return JsonDocument
            .Parse(await tool.RunAsync(
                JsonSerializer.Serialize(new { planName = PlanName }), CancellationToken.None))
            .RootElement;
    }

    [Fact]
    public async Task ALL_THREE_rules_travel_and_the_UNITS_one_is_complete()
    {
        var result = await RunAsync();
        var rules = result.GetProperty("rules");

        rules.GetArrayLength().Should().Be(3, "the plan has three active rules and the payload carries all of them");

        var units = rules.EnumerateArray().Single(r => r.GetProperty("ruleName").GetString() == UnitsRuleName);

        // ★ EVERY FIELD THE MODEL NEEDS TO EXPLAIN IT. "The configuration is not available" was not true
        // of any of these.
        units.GetProperty("measurementType").GetString().Should().Be("Units");
        units.GetProperty("measurementBase").GetString().Should().Be(nameof(MeasurementBase.TransactionQuantity));
        units.GetProperty("rateTable").GetProperty("type").GetString().Should().Be("Flat");
        units.GetProperty("rateTable").GetProperty("semanticBehavior").GetString()
            .Should().Be(nameof(RateSemantic.CurrencyAmountPerUnit));
        units.GetProperty("rateTable").GetProperty("rawValue").GetDecimal().Should().Be(5m);
        units.GetProperty("triggerCondition").GetString().Should().Be("Unconditional");
        units.GetProperty("sortOrder").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task The_payload_is_written_in_READABLE_text_not_escape_sequences()
    {
        // ★ THE OTHER HALF OF WHAT THE MODEL HAD TO READ. The default JSON encoder escapes every
        // non-ASCII character, so a Spanish plan arrived as "Comisión Base Revenue"" — six
        // characters where there was one, on a product whose tenants write in Spanish and Polish. It is
        // not a correctness bug (the JSON parses either way), but it inflates the context and gives a
        // small model a wall of escapes to read a rule name through.
        var tenant = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenant);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(The_payload_is_written_in_READABLE_text_not_escape_sequences)}.{tenant}")
                .Options,
            tenantCtx, Substitute.For<IPublisher>());

        var plan = CompensationPlan.Create(
            tenant, PlanName, "incident repro",
            DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30)),
            "EUR", "seed", Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());
        plan.AddRule(
            "Comisión Base", 1,
            new Measurement { Type = MeasurementType.Revenue }, RateTable.Flat(0.05m));
        plan.Activate("seed", DateTimeOffset.UtcNow, Guid.NewGuid());
        db.CompensationPlans.Add(plan);
        db.SaveChanges();

        var json = await new GetPlanRulesTool(
                new HandlerSender(db, new AllowAll()), NullLogger<GetPlanRulesTool>.Instance)
            .RunAsync(JsonSerializer.Serialize(new { planName = PlanName }), CancellationToken.None);

        json.Should().Contain("Comisión Base").And.NotContain("\\u00F3");
        json.Should().Contain("Q3 2026 — Plan").And.NotContain("\\u2014");
    }
}
