using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.Builders;
using Wasnie.UnitTests.TestDoubles;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// The money assertion for this WI, written against Rodolfo's actual case: €50 / 112 units for a payee
/// on BOTH a Revenue plan and a Units plan. The engine's tie-break silently picked Revenue and paid
/// €2.50. With an explicit selection the credit must land on the plan the admin chose, and the amount
/// must follow from THAT plan's rate table.
/// </summary>
public sealed class CreditAllocationPlanChoiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PayeeId = Guid.NewGuid();
    private static readonly DateOnly TxDate = new(2026, 6, 15);
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        CreditAllocationService Service,
        CompensationPlan RevenuePlan,
        CompensationPlan UnitsPlan,
        PlanAssignment RevenueAssignment,
        PlanAssignment UnitsAssignment,
        IReadOnlyDictionary<Guid, IReadOnlyList<PlanAssignment>> AssignmentsByPayee,
        IReadOnlyDictionary<Guid, CompensationPlan> PlansById);

    private static Fixture BuildFixture(string dbName)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        // Revenue plan: 5% of amount → €50 * 0.05 = €2.50 (the wrong-but-silent result Rodolfo saw).
        var revenuePlan = new PlanBuilder()
            .WithTenantId(TenantId).WithName("Revenue Plan").WithCurrency("EUR")
            .WithPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))
            .Build();
        revenuePlan.AddRule(
            "Revenue 5%", sortOrder: 1,
            measurement: new Measurement
            {
                Type = MeasurementType.Revenue, SourceField = "amount",
                Aggregation = MeasurementAggregation.Sum,
            },
            rateTable: RateTable.Flat(0.05m));

        // Units plan: €1 per unit → 112 units = €112. A completely different answer for the same sale.
        var unitsPlan = new PlanBuilder()
            .WithTenantId(TenantId).WithName("Units Plan").WithCurrency("EUR")
            .WithPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))
            .Build();
        unitsPlan.AddRule(
            "€1 per unit", sortOrder: 1,
            measurement: new Measurement
            {
                Type = MeasurementType.Units, SourceField = "quantity",
                Aggregation = MeasurementAggregation.Sum,
            },
            rateTable: RateTable.Flat(1m));

        // Revenue assignment is deliberately the NARROWER period, so the tie-break prefers it.
        var revenueAssignment = MakeAssignment(revenuePlan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var unitsAssignment = MakeAssignment(unitsPlan.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var service = new CreditAllocationService(
            db, new FakeGuidGenerator(), new FakeClock(Now.UtcDateTime),
            NullLogger<CreditAllocationService>.Instance,
            Substitute.For<IQuotaAttainmentService>());

        return new Fixture(
            service, revenuePlan, unitsPlan, revenueAssignment, unitsAssignment,
            new Dictionary<Guid, IReadOnlyList<PlanAssignment>>
            {
                [PayeeId] = [revenueAssignment, unitsAssignment],
            },
            new Dictionary<Guid, CompensationPlan>
            {
                [revenuePlan.Id] = revenuePlan,
                [unitsPlan.Id] = unitsPlan,
            });
    }

    private static PlanAssignment MakeAssignment(Guid planId, DateOnly start, DateOnly end) =>
        PlanAssignment.Create(
            TenantId, planId, PayeeId,
            PayeeReference.Snapshot(PayeeId, "Test Payee", "E1"),
            DateRange.Of(start, end), "seed", Guid.NewGuid(), Now, Guid.NewGuid());

    // €50 / 112 units, exactly the transaction that exposed the bug.
    private static CompensationTransaction MakeTransaction(Guid? selectedAssignmentId) =>
        CompensationTransaction.Ingest(
            TenantId, "REF-PLAN-1", PayeeId, Money.Of(50m, "EUR"), TxDate,
            TransactionSource.Manual, "admin", Guid.NewGuid(), Now, Guid.NewGuid(),
            quantity: 112, selectedPlanAssignmentId: selectedAssignmentId);

    // SUPERSEDED BEHAVIOUR — this used to assert that no selection still credited Revenue €2.50 via the
    // tie-break, pinned while only the manual path was fixed. The fail-loud WI removed that fallback:
    // with 2+ eligible plans and nobody declaring one, the engine now credits NOTHING rather than
    // guessing. The €2.50 outcome was the bug, so it is no longer something to preserve.
    [Fact]
    public async Task Without_a_selection_two_eligible_plans_now_credit_nothing()
    {
        var f = BuildFixture(nameof(Without_a_selection_two_eligible_plans_now_credit_nothing));

        var credits = await f.Service.AllocateAsync(
            MakeTransaction(selectedAssignmentId: null), f.AssignmentsByPayee, f.PlansById);

        credits.Should().BeEmpty();
    }

    // (b) The fix: choosing the Units plan credits the Units plan, at the Units plan's rate.
    [Fact]
    public async Task Choosing_the_units_plan_credits_the_units_plan()
    {
        var f = BuildFixture(nameof(Choosing_the_units_plan_credits_the_units_plan));

        var credits = await f.Service.AllocateAsync(
            MakeTransaction(f.UnitsAssignment.Id), f.AssignmentsByPayee, f.PlansById);

        credits.Should().ContainSingle();
        credits[0].PlanId.Should().Be(f.UnitsPlan.Id);
        credits[0].CreditedAmount.Amount.Should().Be(112m);
    }

    // The choice is obeyed in both directions — it is not a "prefer Units" rule.
    [Fact]
    public async Task Choosing_the_revenue_plan_credits_the_revenue_plan()
    {
        var f = BuildFixture(nameof(Choosing_the_revenue_plan_credits_the_revenue_plan));

        var credits = await f.Service.AllocateAsync(
            MakeTransaction(f.RevenueAssignment.Id), f.AssignmentsByPayee, f.PlansById);

        credits.Should().ContainSingle();
        credits[0].PlanId.Should().Be(f.RevenuePlan.Id);
        credits[0].CreditedAmount.Amount.Should().Be(2.50m);
    }

    // (e) A selection that stopped being valid must fail loudly. The job's existing DomainException
    // handler turns this into a visible skip; it must NOT quietly credit the other plan.
    [Fact]
    public async Task An_invalid_selection_throws_instead_of_crediting_another_plan()
    {
        var f = BuildFixture(nameof(An_invalid_selection_throws_instead_of_crediting_another_plan));
        f.UnitsAssignment.Deactivate("admin", Now, Guid.NewGuid());

        var act = async () => await f.Service.AllocateAsync(
            MakeTransaction(f.UnitsAssignment.Id), f.AssignmentsByPayee, f.PlansById);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*no longer active*");
    }

    // ── Fail-loud on ambiguity (Excel / HubSpot, where nobody can be asked) ────────────────────

    // (a) The change: with 2+ eligible plans and no declared choice, NOTHING is credited. Previously
    // the tie-break silently produced €2.50 against a plan nobody picked.
    [Fact]
    public async Task Ambiguous_attribution_produces_no_credits_at_all()
    {
        var f = BuildFixture(nameof(Ambiguous_attribution_produces_no_credits_at_all));

        var credits = await f.Service.AllocateAsync(
            MakeExcelTransaction(), f.AssignmentsByPayee, f.PlansById);

        credits.Should().BeEmpty();
    }

    // (b) One eligible plan → unchanged: still credited exactly as before. No regression.
    [Fact]
    public async Task A_single_eligible_plan_is_still_credited_normally()
    {
        var f = BuildFixture(nameof(A_single_eligible_plan_is_still_credited_normally));
        // Remove the Units plan from the picture, leaving exactly one eligible assignment.
        var single = new Dictionary<Guid, IReadOnlyList<PlanAssignment>>
        {
            [PayeeId] = [f.RevenueAssignment],
        };

        var credits = await f.Service.AllocateAsync(MakeExcelTransaction(), single, f.PlansById);

        credits.Should().ContainSingle();
        credits[0].PlanId.Should().Be(f.RevenuePlan.Id);
        credits[0].CreditedAmount.Amount.Should().Be(2.50m);
    }

    // (c) A declared choice is never ambiguous — the manual path keeps working untouched.
    [Fact]
    public async Task A_transaction_with_a_declared_plan_is_never_treated_as_ambiguous()
    {
        var f = BuildFixture(nameof(A_transaction_with_a_declared_plan_is_never_treated_as_ambiguous));

        var credits = await f.Service.AllocateAsync(
            MakeTransaction(f.UnitsAssignment.Id), f.AssignmentsByPayee, f.PlansById);

        credits.Should().ContainSingle();
        credits[0].PlanId.Should().Be(f.UnitsPlan.Id);
    }

    // (e) No eligible plan at all → unchanged (empty, surfaced as NoActiveAssignment elsewhere).
    [Fact]
    public async Task No_eligible_plan_still_produces_no_credits_as_before()
    {
        var f = BuildFixture(nameof(No_eligible_plan_still_produces_no_credits_as_before));
        var none = new Dictionary<Guid, IReadOnlyList<PlanAssignment>>
        {
            [PayeeId] = [],
        };

        var credits = await f.Service.AllocateAsync(MakeExcelTransaction(), none, f.PlansById);

        credits.Should().BeEmpty();
    }

    // Same €50 / 112-unit sale, but arriving from Excel — nobody to ask at load time.
    private static CompensationTransaction MakeExcelTransaction() =>
        CompensationTransaction.Ingest(
            TenantId, "REF-EXCEL-1", PayeeId, Money.Of(50m, "EUR"), TxDate,
            TransactionSource.EtlImport, "import", Guid.NewGuid(), Now, Guid.NewGuid(),
            quantity: 112);
}
