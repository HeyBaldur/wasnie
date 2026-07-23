using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Handlers.Plans;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Aggregate "payees of this plan also in another active plan" — anti-Cartesian, no N+1.
/// </summary>
public sealed class GetMultiPlanPayeesHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _auth;

    public GetMultiPlanPayeesHandlerTests()
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

        _auth = Substitute.For<IAuthorizationService>();
        _auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    public void Dispose() => _db.Dispose();

    private Guid SeedPlan(string name, bool archived)
    {
        var id = Guid.NewGuid();
        var plan = Plan.Create(TenantId, name, "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "EUR", "system", id, Now, Guid.NewGuid());
        plan.AddRule("Base", 1,
            new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
            RateTable.Flat(0.05m));
        plan.Activate("system", Now, Guid.NewGuid());
        if (archived) plan.Archive("system", Now, Guid.NewGuid());
        _db.CompensationPlans.Add(plan);
        _db.SaveChanges();
        return id;
    }

    private void SeedAssignment(Guid payeeId, string code, Guid planId)
    {
        var a = PlanAssignment.Create(TenantId, planId, payeeId,
            PayeeReference.Snapshot(payeeId, $"Payee {code}", code),
            DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31)),
            "system", Guid.NewGuid(), Now, Guid.NewGuid());
        _db.PlanAssignments.Add(a);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Returns_OnlyPayeesOfThisPlan_AlsoInAnotherActivePlan()
    {
        var planA = SeedPlan("Plan A", archived: false);
        var planB = SeedPlan("Plan B", archived: false);
        var planC = SeedPlan("Plan C", archived: true); // archived → must NOT count

        var multi = Guid.NewGuid();      // in A + B (two active) → counts
        var single = Guid.NewGuid();     // in A only → does not count
        var archivedOther = Guid.NewGuid(); // in A + C (C archived) → does not count

        SeedAssignment(multi, "MULTI", planA);
        SeedAssignment(multi, "MULTI", planB);
        SeedAssignment(single, "SINGLE", planA);
        SeedAssignment(archivedOther, "ARCH", planA);
        SeedAssignment(archivedOther, "ARCH", planC);

        var handler = new GetMultiPlanPayeesHandler(_db, _auth);
        var result = await handler.Handle(new GetMultiPlanPayeesQuery(planA), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(1);
        result.Value.Items.Should().HaveCount(1);

        var item = result.Value.Items[0];
        item.PayeeId.Should().Be(multi);
        item.OtherPlans.Should().ContainSingle();
        item.OtherPlans[0].PlanId.Should().Be(planB);
        item.OtherPlans[0].PlanName.Should().Be("Plan B");
    }

    [Fact]
    public async Task Returns_Empty_WhenNoPayeeIsMultiPlan()
    {
        var planA = SeedPlan("Plan A", archived: false);
        SeedPlan("Plan B", archived: false);

        SeedAssignment(Guid.NewGuid(), "ONLY-A", planA);

        var handler = new GetMultiPlanPayeesHandler(_db, _auth);
        var result = await handler.Handle(new GetMultiPlanPayeesQuery(planA), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(0);
        result.Value.Items.Should().BeEmpty();
    }
}
