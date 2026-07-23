using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.Handlers.Plans;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Archiving a plan must deactivate its assignments so the archived plan can no longer be
/// resolved/processed (credits were being mis-attributed to archived plans otherwise).
/// </summary>
public sealed class ArchivePlanHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _auth;
    private readonly ICurrentUserService _currentUser;
    private readonly IClock _clock;
    private readonly IGuidGenerator _guid;

    public ArchivePlanHandlerTests()
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

        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.UserId.Returns("user-1");

        _clock = Substitute.For<IClock>();
        _clock.UtcNowOffset.Returns(Now);

        _guid = Substitute.For<IGuidGenerator>();
        _guid.NewGuid().Returns(_ => Guid.NewGuid());
    }

    public void Dispose() => _db.Dispose();

    private void SeedActivePlan(Guid planId)
    {
        var plan = Plan.Create(TenantId, "Test Plan", "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "EUR", "system", planId, Now, Guid.NewGuid());
        plan.AddRule("Base", 1,
            new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
            RateTable.Flat(0.05m));
        plan.Activate("system", Now, Guid.NewGuid());
        _db.CompensationPlans.Add(plan);
        _db.SaveChanges();
    }

    private PlanAssignment SeedActiveAssignment(Guid payeeId, Guid planId)
    {
        var a = PlanAssignment.Create(TenantId, planId, payeeId,
            PayeeReference.Snapshot(payeeId, "Payee", "EMP-1"),
            DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31)),
            "system", Guid.NewGuid(), Now, Guid.NewGuid());
        _db.PlanAssignments.Add(a);
        _db.SaveChanges();
        return a;
    }

    [Fact]
    public async Task Archive_DeactivatesActiveAssignmentsOfThePlan()
    {
        var planId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        SeedActivePlan(planId);
        var assignment = SeedActiveAssignment(payeeId, planId);

        var handler = new ArchivePlanHandler(_db, _currentUser, _clock, _guid, _auth);
        var result = await handler.Handle(new ArchivePlanCommand(planId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var reloadedPlan = await _db.CompensationPlans.FirstAsync(p => p.Id == planId);
        reloadedPlan.Status.Should().Be(PlanStatus.Archived);

        var reloadedAssignment = await _db.PlanAssignments.FirstAsync(a => a.Id == assignment.Id);
        reloadedAssignment.Status.Should().Be(AssignmentStatus.Deactivated);
    }

    [Fact]
    public async Task Archive_DoesNotTouchAssignmentsOfOtherPlans()
    {
        var planId = Guid.NewGuid();
        var otherPlanId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        SeedActivePlan(planId);
        var otherAssignment = SeedActiveAssignment(payeeId, otherPlanId);

        var handler = new ArchivePlanHandler(_db, _currentUser, _clock, _guid, _auth);
        await handler.Handle(new ArchivePlanCommand(planId), CancellationToken.None);

        var reloaded = await _db.PlanAssignments.FirstAsync(a => a.Id == otherAssignment.Id);
        reloaded.Status.Should().Be(AssignmentStatus.Active);
    }
}
