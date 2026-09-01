using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.Handlers.Plans;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Application.Common.Models;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Exceptions;
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
    /// <summary>
    /// The archive confirmation dialog shows PlanDto.ActiveAssignmentCount. That number is a promise
    /// about what the button is going to do, so it has to be the SAME set ArchivePlanHandler
    /// deactivates — not merely a plausible number. This test asserts the two against each other:
    /// count first, then archive, then check exactly those assignments flipped.
    /// </summary>
    [Fact]
    public async Task ActiveAssignmentCount_EqualsWhatArchivingActuallyDeactivates()
    {
        var planId = Guid.NewGuid();
        SeedActivePlan(planId);

        var first = SeedActiveAssignment(Guid.NewGuid(), planId);
        var second = SeedActiveAssignment(Guid.NewGuid(), planId);

        // Noise that must NOT be counted: an already-deactivated assignment on the same plan...
        var alreadyOff = SeedActiveAssignment(Guid.NewGuid(), planId);
        alreadyOff.Deactivate("system", Now, Guid.NewGuid());
        // ...and an active assignment belonging to a different plan.
        var otherPlan = SeedActiveAssignment(Guid.NewGuid(), Guid.NewGuid());
        await _db.SaveChangesAsync();

        var read = new GetPlanByIdHandler(_db, _auth);
        var dto = await read.Handle(new GetPlanByIdQuery(planId), CancellationToken.None);

        dto.IsSuccess.Should().BeTrue();
        dto.Value!.ActiveAssignmentCount.Should().Be(2);

        var archive = new ArchivePlanHandler(_db, _currentUser, _clock, _guid, _auth);
        (await archive.Handle(new ArchivePlanCommand(planId), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        // Exactly the two that were counted, and nothing else.
        var deactivatedNow = await _db.PlanAssignments
            .Where(a => new[] { first.Id, second.Id }.Contains(a.Id))
            .ToListAsync();
        deactivatedNow.Should().OnlyContain(a => a.Status == AssignmentStatus.Deactivated);

        (await _db.PlanAssignments.FirstAsync(a => a.Id == otherPlan.Id))
            .Status.Should().Be(AssignmentStatus.Active);
    }

    /// <summary>
    /// Zero is a real answer, not a missing one: the UI swaps to a different message at 0, so the
    /// handler must report 0 rather than leaving the field at whatever a mapper happened to pass.
    /// </summary>
    [Fact]
    public async Task ActiveAssignmentCount_IsZero_WhenPlanHasNoAssignments()
    {
        var planId = Guid.NewGuid();
        SeedActivePlan(planId);

        var read = new GetPlanByIdHandler(_db, _auth);
        var dto = await read.Handle(new GetPlanByIdQuery(planId), CancellationToken.None);

        dto.IsSuccess.Should().BeTrue();
        dto.Value!.ActiveAssignmentCount.Should().Be(0);
    }

    /// <summary>
    /// The list screen archives plans too, from its own row menu, so its DTO carries the same number.
    /// </summary>
    [Fact]
    public async Task ListPlans_CarriesActiveAssignmentCount_PerPlan()
    {
        var planId = Guid.NewGuid();
        SeedActivePlan(planId);
        SeedActiveAssignment(Guid.NewGuid(), planId);
        SeedActiveAssignment(Guid.NewGuid(), planId);

        var handler = new ListPlansHandler(_db, _auth);
        var result = await handler.Handle(
            new ListPlansQuery(new PaginationQuery { Page = 1, PageSize = 20 }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Single(x => x.Id == planId).ActiveAssignmentCount.Should().Be(2);
    }

    /// <summary>
    /// The archive date is STORED, not reconstructed. It used to live only in AuditLogs, and that
    /// table is purgeable — losing it would lose the line between a sale that still pays through this
    /// plan and one that does not (KAN-31).
    /// </summary>
    [Fact]
    public async Task Archive_StampsArchivedAt_WithTheArchiveInstant()
    {
        var planId = Guid.NewGuid();
        SeedActivePlan(planId);

        var before = await _db.CompensationPlans.AsNoTracking().FirstAsync(p => p.Id == planId);
        before.ArchivedAt.Should().BeNull("an Active plan has no archive date");

        var handler = new ArchivePlanHandler(_db, _currentUser, _clock, _guid, _auth);
        (await handler.Handle(new ArchivePlanCommand(planId), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        var after = await _db.CompensationPlans.FirstAsync(p => p.Id == planId);
        after.ArchivedAt.Should().Be(Now);
    }

    /// <summary>
    /// ★ APPEND-ONLY. Archiving is terminal — Activate() accepts Draft only — so nothing can un-archive
    /// a plan and nothing may clear this date. This pins the guard that makes the backfill sound: if
    /// some future change let an Archived plan go back to Active, UpdatedAt would stop being the
    /// archive instant and B29's backfill reasoning would silently rot.
    /// </summary>
    [Fact]
    public async Task ArchivedPlan_CannotBeReactivated()
    {
        var planId = Guid.NewGuid();
        SeedActivePlan(planId);

        var handler = new ArchivePlanHandler(_db, _currentUser, _clock, _guid, _auth);
        await handler.Handle(new ArchivePlanCommand(planId), CancellationToken.None);

        var plan = await _db.CompensationPlans.FirstAsync(p => p.Id == planId);
        var reactivate = () => plan.Activate("system", Now.AddDays(1), Guid.NewGuid());

        reactivate.Should().Throw<DomainException>();
        plan.ArchivedAt.Should().Be(Now, "the archive date survives a rejected reactivation");
    }
}
