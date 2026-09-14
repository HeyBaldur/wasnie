using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Sandbox;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.Builders;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// El paso «calcular» del sandbox, cuando una venta NO genera comisión.
///
/// ★★ LO QUE SE PRUEBA ES QUE YA NO ES SILENCIO (§B1). Antes, cero créditos era un `continue`: la venta
/// quedaba pendiente y la pantalla no decía nada. Un usuario que había puesto en la regla un disparador
/// que la venta no cumplía no tenía forma de saber por qué. Ahora cada venta vuelve con su motivo, y el
/// motivo sale de las mismas piezas que usa el motor — el explicador de cálculo es el real.
/// </summary>
public sealed class CalculateSandboxPendingTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly SaleDate = new(2026, 5, 1);

    private static ApplicationDbContext NewDb(string name)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());
    }

    /// <summary>
    /// El handler con el asignador de créditos devolviendo SIEMPRE cero — el caso que se diagnostica —
    /// y el explicador REAL, para que «el disparador no se cumplió» lo decida el motor y no el test.
    /// </summary>
    private static CalculateSandboxPendingHandler NewHandler(ApplicationDbContext db)
    {
        var allocation = Substitute.For<ICreditAllocationService>();
        allocation.AllocateAsync(Arg.Any<CompensationTransaction>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Credit>>(Array.Empty<Credit>()));

        return new CalculateSandboxPendingHandler(
            db,
            allocation,
            new RuleCalculationExplainer(NullLogger<RuleCalculationExplainer>.Instance),
            Substitute.For<ICurrentUserService>(),
            new FakeClock(Now),
            Substitute.For<IGuidGenerator>());
    }

    private static Guid SeedPayee(ApplicationDbContext db)
    {
        var p = Payee.Create(TenantId, "Alex Demo", "DEMO-001", null, null, "seed", Guid.NewGuid(), new DateTimeOffset(Now));
        db.Payees.Add(p);
        db.SaveChanges();
        return p.Id;
    }

    private static Guid SeedPlan(ApplicationDbContext db, Action<Wasnie.Domain.Compensation.Plans.Plan>? addRules = null)
    {
        var plan = new PlanBuilder().WithTenantId(TenantId).WithName("Demo plan").WithCurrency("EUR")
            .WithPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).Build();
        addRules?.Invoke(plan);
        db.CompensationPlans.Add(plan);
        db.SaveChanges();
        return plan.Id;
    }

    private static void SeedAssignment(ApplicationDbContext db, Guid planId, Guid payeeId)
    {
        var a = PlanAssignment.Create(
            TenantId, planId, payeeId, PayeeReference.Snapshot(payeeId, "Alex Demo", "DEMO-001"),
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "seed", Guid.NewGuid(), new DateTimeOffset(Now), Guid.NewGuid());
        db.PlanAssignments.Add(a);
        db.SaveChanges();
    }

    private static void SeedPendingSale(ApplicationDbContext db, Guid? payeeId, decimal amount = 10_000m)
    {
        var tx = CompensationTransaction.Ingest(
            TenantId, "DEMO-0001", payeeId, Money.Of(amount, "EUR"), SaleDate,
            TransactionSource.Manual, "seed", Guid.NewGuid(), new DateTimeOffset(Now), Guid.NewGuid());
        db.CompensationTransactions.Add(tx);
        db.SaveChanges();
    }

    /// <summary>Una regla plana del 5 % que sólo paga ventas por encima de 50.000.</summary>
    private static void AddBigDealsRule(Wasnie.Domain.Compensation.Plans.Plan plan) =>
        plan.AddRule(
            name: "Big deals",
            sortOrder: 1,
            measurement: new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
            rateTable: RateTable.Flat(0.05m),
            trigger: Trigger.When(LogicalOperator.And,
            [
                new Condition
                {
                    Field = "transactionamount",
                    Operator = ConditionOperator.GreaterThan,
                    Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "50000" },
                },
            ]));

    [Fact]
    public async Task A_sale_that_misses_the_trigger_comes_back_with_the_trigger_and_the_rule()
    {
        // ★ EL CASO DEL USUARIO: la regla lleva un disparador, la venta de 10.000 no lo cumple.
        var db = NewDb(nameof(A_sale_that_misses_the_trigger_comes_back_with_the_trigger_and_the_rule));
        var payee = SeedPayee(db);
        var plan = SeedPlan(db, AddBigDealsRule);
        SeedAssignment(db, plan, payee);
        SeedPendingSale(db, payee);

        var result = await NewHandler(db).Handle(new CalculateSandboxPendingCommand(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CreditsCreated.Should().Be(0);
        var sale = result.Value.Uncredited.Should().ContainSingle().Subject;
        sale.ReferenceNumber.Should().Be("DEMO-0001");
        sale.Reason.Should().Be(SandboxUncreditedReason.TriggerNotMatched);
        sale.RuleNames.Should().Equal("Big deals");
    }

    [Fact]
    public async Task A_payee_with_no_assignment_is_reported_as_such()
    {
        var db = NewDb(nameof(A_payee_with_no_assignment_is_reported_as_such));
        var payee = SeedPayee(db);
        SeedPlan(db, AddBigDealsRule);
        SeedPendingSale(db, payee);

        var result = await NewHandler(db).Handle(new CalculateSandboxPendingCommand(), default);

        result.Value!.Uncredited.Should().ContainSingle()
            .Which.Reason.Should().Be(SandboxUncreditedReason.NoActiveAssignment);
    }

    [Fact]
    public async Task A_plan_with_no_rules_is_reported_as_such()
    {
        var db = NewDb(nameof(A_plan_with_no_rules_is_reported_as_such));
        var payee = SeedPayee(db);
        var plan = SeedPlan(db);
        SeedAssignment(db, plan, payee);
        SeedPendingSale(db, payee);

        var result = await NewHandler(db).Handle(new CalculateSandboxPendingCommand(), default);

        result.Value!.Uncredited.Should().ContainSingle()
            .Which.Reason.Should().Be(SandboxUncreditedReason.NoApplicableRules);
    }

    [Fact]
    public async Task A_sale_with_no_payee_is_reported_as_such()
    {
        var db = NewDb(nameof(A_sale_with_no_payee_is_reported_as_such));
        SeedPendingSale(db, payeeId: null);

        var result = await NewHandler(db).Handle(new CalculateSandboxPendingCommand(), default);

        result.Value!.Uncredited.Should().ContainSingle()
            .Which.Reason.Should().Be(SandboxUncreditedReason.NoPayee);
    }
}
