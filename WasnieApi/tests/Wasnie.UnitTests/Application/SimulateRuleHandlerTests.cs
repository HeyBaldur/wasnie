using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Plans;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Application.Compensation.Validators.Plans;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Simulating a rule: the real engine, a hypothetical transaction, and nothing written.
///
/// ★★ THE FEATURE EXISTS BECAUSE A RULE LIKE "Flat 5% + modifier ×1.2 + cap 10,000 + floor 100"
/// cannot be worked out in anybody's head, and until now the only way to learn what it pays was to
/// wait for a real transaction to be processed. The danger it introduces is a calculator that
/// promises one number while the system pays another, which in a commission product is worse than
/// having no calculator — so every test here is about the answer coming from the engine itself.
/// </summary>
public sealed class SimulateRuleHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string EUR = "EUR";

    private readonly ApplicationDbContext _db;
    private readonly SimulateRuleHandler _handler;
    private readonly Guid _planId = Guid.NewGuid();

    public SimulateRuleHandlerTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        tenantCtx.IsResolved.Returns(true);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        var auth = Substitute.For<IAuthorizationService>();
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var clock = Substitute.For<IClock>();
        clock.UtcNowOffset.Returns(Now);

        var guid = Substitute.For<IGuidGenerator>();
        guid.NewGuid().Returns(_ => Guid.NewGuid());

        _handler = new SimulateRuleHandler(
            _db, auth,
            new RuleCalculationExplainer(NullLogger<RuleCalculationExplainer>.Instance),
            guid, clock);

        SeedPlan();
    }

    public void Dispose() => _db.Dispose();

    private void SeedPlan()
    {
        var plan = Plan.Create(
            TenantId, "Simulated plan", "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            EUR, "system", _planId, Now, Guid.NewGuid());

        _db.CompensationPlans.Add(plan);
        _db.SaveChanges();
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private SimulateRuleQuery Query(
        RateTable? table = null,
        Modifier? modifier = null,
        Cap? cap = null,
        Floor? floor = null,
        Trigger? trigger = null,
        MeasurementType measurement = MeasurementType.Revenue,
        decimal amount = 1200m,
        int quantity = 1,
        decimal? attainment = null,
        decimal? priorCumulative = null,
        decimal? quotaTarget = null,
        Guid? planId = null) =>
        new(
            PlanId: planId ?? _planId,
            Name: "Simulated rule",
            Measurement: new Measurement { Type = measurement },
            RateTable: table ?? RateTable.Flat(0.05m),
            Trigger: trigger,
            Modifier: modifier,
            Cap: cap,
            Floor: floor,
            Amount: amount,
            Quantity: quantity,
            AttainmentPct: attainment,
            PriorCumulative: priorCumulative,
            QuotaTarget: quotaTarget);

    private static Cap CapOf(decimal a, CapScope scope = CapScope.PerTransaction) =>
        new() { Amount = Money.Of(a, EUR), Scope = scope };

    private static Floor FloorOf(decimal a) => new() { Amount = Money.Of(a, EUR) };

    private static Modifier ModOf(decimal f) => new() { Type = ModifierType.Multiplier, Factor = f };

    private static RuleSimulationStepDto Step(RuleSimulationDto d, RuleCalculationComponent c) =>
        d.Steps.Single(s => s.Component == c);

    private static RateTable Attainment(bool split = false) => new()
    {
        Type = RateTableType.AttainmentBased,
        SplitAtQuota = split,
        AttainmentTiers = new List<AttainmentTier>
        {
            new() { AttainmentFrom = 0m,    AttainmentTo = 1.00m, Rate = 0.02m },
            new() { AttainmentFrom = 1.00m, AttainmentTo = null,  Rate = 0.08m },
        },
    };

    // ══ ★ The rule from the screenshot ════════════════════════════════════

    [Fact]
    public async Task THE_FLOOR_WINS_OVER_THE_CAP_AND_THE_BREAKDOWN_SHOWS_BOTH_IN_THAT_ORDER()
    {
        // ★★ 5% of 1,200 = 60 → ×1.2 = 72 → cap 10,000 does not bite → floor 100 lifts it to 100.
        // The steps arrive in the order the ENGINE ran them. A client that assembled this cascade
        // from the rule's own fields would put the floor before the cap, report 72, and teach the
        // reader an order the product does not use.
        var result = await _handler.Handle(
            Query(RateTable.Flat(0.05m), ModOf(1.2m), CapOf(10_000m), FloorOf(100m)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;

        dto.Simulated.Should().BeTrue();
        dto.Blocker.Should().Be(RuleSimulationBlocker.None);
        dto.CommissionAmount.Should().Be(100m);
        dto.Currency.Should().Be(EUR);

        dto.Steps.Select(s => s.Component).Should().Equal(
            RuleCalculationComponent.Trigger,
            RuleCalculationComponent.Base,
            RuleCalculationComponent.Rate,
            RuleCalculationComponent.Modifier,
            RuleCalculationComponent.Cap,
            RuleCalculationComponent.Floor);

        Step(dto, RuleCalculationComponent.Rate).OutputAmount.Should().Be(60m);
        Step(dto, RuleCalculationComponent.Modifier).OutputAmount.Should().Be(72m);

        var cap = Step(dto, RuleCalculationComponent.Cap);
        cap.Outcome.Should().Be(RuleCalculationOutcome.AppliedWithoutEffect);
        cap.OutputAmount.Should().Be(72m);

        var floor = Step(dto, RuleCalculationComponent.Floor);
        floor.Outcome.Should().Be(RuleCalculationOutcome.Applied);
        floor.OutputAmount.Should().Be(100m);
    }

    [Fact]
    public async Task A_high_amount_makes_the_cap_bite()
    {
        var result = await _handler.Handle(
            Query(RateTable.Flat(0.05m), null, CapOf(10_000m), null, amount: 1_000_000m),
            CancellationToken.None);

        result.Value!.CommissionAmount.Should().Be(10_000m);
        Step(result.Value!, RuleCalculationComponent.Cap).Outcome
            .Should().Be(RuleCalculationOutcome.Applied);
    }

    // ══ ★ It is the engine answering, not a parallel calculation ══════════

    [Fact]
    public async Task THE_ENDPOINT_RETURNS_WHAT_THE_ENGINE_RETURNS()
    {
        // ★ THE RULE THIS WHOLE FEATURE HANGS ON. Asked the same question directly, the engine gives
        // the same answer — because the handler asks it rather than reproducing it. Two commission
        // engines that agree today are two commission engines that disagree eventually, and the one
        // people consult before saving a plan would be the one that is wrong.
        var query = Query(RateTable.Flat(0.07m), ModOf(1.35m), CapOf(500m), FloorOf(25m), amount: 4_321m);

        var throughHandler = (await _handler.Handle(query, CancellationToken.None)).Value!;

        var scratch = Plan.Create(
            TenantId, "direct", "d", DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            EUR, "t", Guid.NewGuid(), Now, Guid.NewGuid());
        var rule = scratch.AddRule("direct", 0, query.Measurement, query.RateTable,
            query.Trigger, query.Modifier, query.Cap, query.Floor);

        var tx = CompensationTransaction.Ingest(
            TenantId, "DIRECT", Guid.NewGuid(), Money.Of(query.Amount, EUR),
            new DateOnly(2026, 6, 1), TransactionSource.Manual, "t",
            Guid.NewGuid(), Now, Guid.NewGuid());

        var direct = new RuleCalculationExplainer(NullLogger<RuleCalculationExplainer>.Instance)
            .Explain(rule, tx, EUR);

        throughHandler.CommissionAmount.Should().Be(direct.Commission!.Amount);
        throughHandler.Steps.Select(s => s.Component)
            .Should().Equal(direct.Steps.Select(s => s.Component));
    }

    // ══ ★★ Attainment: the refusal ════════════════════════════════════════

    [Fact]
    public async Task AN_ATTAINMENT_RULE_WITHOUT_CONTEXT_IS_REFUSED_NOT_ANSWERED_AT_ONE_HUNDRED_PERCENT()
    {
        // ★★ THE FAILURE THIS WORK ITEM EXISTS TO PREVENT. The engine's default attainment is 1.0, so
        // "just simulate it" would not error — it would confidently report the commission of a rep at
        // full quota and present it as anybody's. Those numbers look completely reasonable, which is
        // exactly what makes them dangerous. Refusing is the only honest answer.
        var result = await _handler.Handle(Query(Attainment()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("a refusal is an answer, not an error");
        result.Value!.Simulated.Should().BeFalse();
        result.Value!.Blocker.Should().Be(RuleSimulationBlocker.AttainmentContextRequired);
        result.Value!.CommissionAmount.Should().BeNull();
        result.Value!.Steps.Should().BeEmpty("there is no breakdown of a calculation that did not run");
    }

    [Fact]
    public async Task With_attainment_supplied_it_simulates_and_marks_the_figure_as_supplied()
    {
        var result = await _handler.Handle(Query(Attainment(), attainment: 0.40m), CancellationToken.None);

        result.Value!.Simulated.Should().BeTrue();
        result.Value!.CommissionAmount.Should().Be(24m, "2% of 1,200 in the below-quota bracket");

        var rate = Step(result.Value!, RuleCalculationComponent.Rate);
        rate.AttainmentSource.Should().Be(AttainmentSource.Supplied,
            "the reader has to be able to tell an assumption from a measurement");
    }

    [Fact]
    public async Task Split_at_quota_without_its_context_is_refused_with_its_own_reason()
    {
        var result = await _handler.Handle(Query(Attainment(split: true)), CancellationToken.None);

        result.Value!.Simulated.Should().BeFalse();
        result.Value!.Blocker.Should().Be(RuleSimulationBlocker.SplitQuotaContextRequired);
    }

    [Fact]
    public async Task Split_at_quota_WITH_its_context_simulates()
    {
        var result = await _handler.Handle(
            Query(Attainment(split: true), amount: 40_000m,
                  priorCumulative: 80_000m, quotaTarget: 100_000m),
            CancellationToken.None);

        result.Value!.Simulated.Should().BeTrue();
        // 20,000 below quota @2% + 20,000 above @8%.
        result.Value!.CommissionAmount.Should().Be(2_000m);
        Step(result.Value!, RuleCalculationComponent.Rate).Tiers.Should().HaveCount(2);
    }

    // ══ Tiered needs nothing external ═════════════════════════════════════

    [Fact]
    public async Task Tiered_simulates_without_any_outside_context()
    {
        var table = RateTable.Tiered(new List<RateTier>
        {
            new() { From = 0m,      To = 10_000m, Rate = 0.05m },
            new() { From = 10_000m, To = null,    Rate = 0.08m },
        });

        var result = await _handler.Handle(Query(table, amount: 30_000m), CancellationToken.None);

        result.Value!.Simulated.Should().BeTrue();
        result.Value!.CommissionAmount.Should().Be(2_100m);

        var rate = Step(result.Value!, RuleCalculationComponent.Rate);
        rate.Tiers.Should().HaveCount(2);
        rate.Tiers!.Select(t => t.Amount).Should().Equal(500m, 1_600m);
    }

    // ══ ★ No commission is not commission of zero ═════════════════════════

    [Fact]
    public async Task A_TRIGGER_THAT_DOES_NOT_MATCH_IS_REPORTED_AS_NO_CREDIT_AT_ALL()
    {
        var trigger = Trigger.When(LogicalOperator.And, new List<Condition>
        {
            new()
            {
                Field = "Amount",
                Operator = ConditionOperator.GreaterThan,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "5000" },
            },
        });

        var result = await _handler.Handle(Query(trigger: trigger, amount: 1_200m), CancellationToken.None);

        result.Value!.Simulated.Should().BeTrue();
        result.Value!.CreditGenerated.Should().BeFalse();
        result.Value!.CommissionAmount.Should().BeNull(
            "the rule did not apply — that is not the same as applying and paying zero");
        result.Value!.Steps.Should().ContainSingle();
        result.Value!.Steps[0].Outcome.Should().Be(RuleCalculationOutcome.NotMatched);
    }

    // ══ Units ═════════════════════════════════════════════════════════════

    [Fact]
    public async Task Units_uses_the_quantity_and_the_per_unit_rate()
    {
        var result = await _handler.Handle(
            Query(RateTable.Flat(5.00m), measurement: MeasurementType.Units,
                  amount: 78_500m, quantity: 3),
            CancellationToken.None);

        result.Value!.CommissionAmount.Should().Be(15m);

        var rate = Step(result.Value!, RuleCalculationComponent.Rate);
        rate.Operand.Should().Be(3m);
        rate.ThresholdAmount.Should().Be(5.00m);
    }

    // ══ ★ The same rules as saving ════════════════════════════════════════

    [Fact]
    public async Task A_CAP_SCOPE_THE_SYSTEM_WOULD_REFUSE_TO_SAVE_IS_REFUSED_HERE_TOO()
    {
        // ★ Otherwise the preview answers for a configuration that can never exist, and the number
        // is a fantasy about a rule nobody will ever be paid under.
        var result = await _handler.Handle(
            Query(cap: CapOf(10m, CapScope.PerPeriod)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Per Transaction");
    }

    [Fact]
    public async Task A_definition_the_DOMAIN_rejects_is_rejected_here_by_the_domain_itself()
    {
        // Units with a non-Flat table is invalid at save time. The handler does not re-check that —
        // it builds the rule through Plan.AddRule, so whatever the domain refuses, this refuses.
        var tiered = RateTable.Tiered(new List<RateTier>
        {
            new() { From = 0m, To = null, Rate = 0.05m },
        });

        var result = await _handler.Handle(
            Query(tiered, measurement: MeasurementType.Units), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void The_validator_mirrors_the_save_time_one_and_adds_the_simulation_inputs()
    {
        var validator = new SimulateRuleQueryValidator();

        validator.Validate(Query(amount: -1m)).IsValid.Should().BeFalse();
        validator.Validate(Query(quantity: 0)).IsValid.Should().BeFalse();
        validator.Validate(Query(attainment: -0.5m)).IsValid.Should().BeFalse();
        validator.Validate(Query(quotaTarget: 0m)).IsValid.Should().BeFalse();
        validator.Validate(Query()).IsValid.Should().BeTrue();
    }

    // ══ ★★ Nothing is written ═════════════════════════════════════════════

    [Fact]
    public async Task SIMULATING_WRITES_NOTHING_AT_ALL()
    {
        // ★★ A PREVIEW THAT CAN MOVE MONEY IS NOT A PREVIEW. Counted across every table the engine
        // touches on the real path, before and after.
        var before = (
            Credits: _db.Credits.Count(),
            Transactions: _db.CompensationTransactions.Count(),
            Plans: _db.CompensationPlans.Count());

        await _handler.Handle(
            Query(RateTable.Flat(0.05m), ModOf(1.2m), CapOf(10_000m), FloorOf(100m)),
            CancellationToken.None);
        await _handler.Handle(Query(Attainment(), attainment: 1.2m), CancellationToken.None);

        var after = (
            Credits: _db.Credits.Count(),
            Transactions: _db.CompensationTransactions.Count(),
            Plans: _db.CompensationPlans.Count());

        after.Should().Be(before);
        _db.ChangeTracker.HasChanges().Should().BeFalse(
            "not even a pending change — the scratch plan and transaction never meet the context");
    }

    // ══ ★ Tenant scoping ══════════════════════════════════════════════════

    [Fact]
    public async Task A_PLAN_FROM_ANOTHER_TENANT_CANNOT_BE_SIMULATED_AGAINST()
    {
        // ★ AND THE BOUNDARY IS THE QUERY ITSELF, not a branch that could be forgotten: the global
        // filter means the row is simply not there.
        var foreign = Plan.Create(
            Guid.NewGuid(), "Someone else's plan", "d",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            EUR, "system", Guid.NewGuid(), Now, Guid.NewGuid());

        _db.CompensationPlans.Add(foreign);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var result = await _handler.Handle(Query(planId: foreign.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }
}
