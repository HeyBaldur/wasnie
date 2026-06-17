using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Assignments;
using Wasnie.Application.Compensation.Handlers.Assignments;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

public sealed class BulkAssignmentHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _auth;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly IGuidGenerator _guid;

    public BulkAssignmentHandlerTests()
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
        _auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);

        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.Email.Returns("admin@test.com");
        _currentUser.UserId.Returns("user-1");

        _audit = Substitute.For<IAuditService>();
        _audit.LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
              .Returns(Task.CompletedTask);

        _clock = Substitute.For<IClock>();
        _clock.UtcNowOffset.Returns(Now);

        _guid = Substitute.For<IGuidGenerator>();
        _guid.NewGuid().Returns(_ => Guid.NewGuid());
    }

    public void Dispose() => _db.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private PlanAssignment SeedAssignment(Guid payeeId, Guid planId, DateOnly start, DateOnly end)
    {
        var snapshot = PayeeReference.Snapshot(payeeId, "Test Payee", "EMP-1");
        var period   = DateRange.Of(start, end);
        var a = PlanAssignment.Create(TenantId, planId, payeeId, snapshot, period,
            "system", Guid.NewGuid(), Now, Guid.NewGuid());
        _db.PlanAssignments.Add(a);
        _db.SaveChanges();
        return a;
    }

    private void SeedPayout(Guid payeeId, Guid planId, DateOnly start, DateOnly end)
    {
        var snapshot = PayeeReference.Snapshot(payeeId, "Test Payee", "EMP-1");
        var period   = DateRange.Of(start, end);
        var payout   = CompensationPayout.Calculate(
            TenantId, payeeId, planId, snapshot, period,
            [], "EUR", "system", Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);
        _db.CompensationPayouts.Add(payout);
        _db.SaveChanges();
    }

    // ── Domain: Activate() ────────────────────────────────────────────────────

    [Fact]
    public void Activate_WhenDeactivated_SetsStatusToActive()
    {
        var payeeId = Guid.NewGuid();
        var planId  = Guid.NewGuid();
        var a = PlanAssignment.Create(TenantId, planId, payeeId,
            PayeeReference.Snapshot(payeeId, "Name", "EMP"),
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)),
            "system", Guid.NewGuid(), Now, Guid.NewGuid());

        a.Deactivate("system", Now, Guid.NewGuid());
        a.Status.Should().Be(AssignmentStatus.Deactivated);

        a.Activate("system", Now.AddSeconds(1), Guid.NewGuid());
        a.Status.Should().Be(AssignmentStatus.Active);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_ThrowsDomainException()
    {
        var payeeId = Guid.NewGuid();
        var planId  = Guid.NewGuid();
        var a = PlanAssignment.Create(TenantId, planId, payeeId,
            PayeeReference.Snapshot(payeeId, "Name", "EMP"),
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)),
            "system", Guid.NewGuid(), Now, Guid.NewGuid());

        var act = () => a.Activate("system", Now, Guid.NewGuid());
        act.Should().Throw<DomainException>();
    }

    // ── BulkDeleteAssignmentsHandler ──────────────────────────────────────────

    [Fact]
    public async Task BulkDelete_WhenNoneUsed_DeletesAllAndReturnsAllDeleted()
    {
        var payeeId = Guid.NewGuid();
        var planId  = Guid.NewGuid();
        var a1 = SeedAssignment(payeeId, planId, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var a2 = SeedAssignment(payeeId, planId, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));

        var handler = new BulkDeleteAssignmentsHandler(_db, _currentUser, _auth, _audit);
        var cmd     = new BulkDeleteAssignmentsCommand([a1.Id, a2.Id]);
        var result  = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AllDeleted.Should().BeTrue();
        result.Value.DeletedCount.Should().Be(2);
        result.Value.Blocked.Should().BeEmpty();
        _db.PlanAssignments.Count().Should().Be(0);
    }

    [Fact]
    public async Task BulkDelete_WhenOneAssignmentUsedInPayout_BlocksAll()
    {
        var payeeId = Guid.NewGuid();
        var planId  = Guid.NewGuid();
        // a1 overlaps with an existing payout → blocked
        var a1 = SeedAssignment(payeeId, planId, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        // a2 has no payout → would be safe alone
        var a2 = SeedAssignment(payeeId, planId, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));

        // Payout overlaps with a1's period
        SeedPayout(payeeId, planId, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));

        var handler = new BulkDeleteAssignmentsHandler(_db, _currentUser, _auth, _audit);
        var cmd     = new BulkDeleteAssignmentsCommand([a1.Id, a2.Id]);
        var result  = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AllDeleted.Should().BeFalse();
        result.Value.DeletedCount.Should().Be(0);
        result.Value.Blocked.Should().HaveCount(1);
        result.Value.Blocked[0].AssignmentId.Should().Be(a1.Id);
        // Neither assignment was deleted
        _db.PlanAssignments.Count().Should().Be(2);
    }

    [Fact]
    public async Task BulkDelete_WhenAllAssignmentsUsed_BlocksAll()
    {
        var payeeId = Guid.NewGuid();
        var planId  = Guid.NewGuid();
        var a1 = SeedAssignment(payeeId, planId, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var a2 = SeedAssignment(payeeId, planId, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));

        SeedPayout(payeeId, planId, new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28));
        SeedPayout(payeeId, planId, new DateOnly(2025, 8, 1), new DateOnly(2025, 8, 31));

        var handler = new BulkDeleteAssignmentsHandler(_db, _currentUser, _auth, _audit);
        var result  = await handler.Handle(new BulkDeleteAssignmentsCommand([a1.Id, a2.Id]), CancellationToken.None);

        result.Value!.AllDeleted.Should().BeFalse();
        result.Value.Blocked.Should().HaveCount(2);
        _db.PlanAssignments.Count().Should().Be(2);
    }

    [Fact]
    public async Task BulkDelete_PayoutForDifferentPlan_DoesNotBlock()
    {
        var payeeId     = Guid.NewGuid();
        var planId      = Guid.NewGuid();
        var otherPlanId = Guid.NewGuid();
        var a1 = SeedAssignment(payeeId, planId, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));

        // Payout for same payee but different plan — should NOT block
        SeedPayout(payeeId, otherPlanId, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));

        var handler = new BulkDeleteAssignmentsHandler(_db, _currentUser, _auth, _audit);
        var result  = await handler.Handle(new BulkDeleteAssignmentsCommand([a1.Id]), CancellationToken.None);

        result.Value!.AllDeleted.Should().BeTrue();
        _db.PlanAssignments.Count().Should().Be(0);
    }

    [Fact]
    public async Task BulkDelete_EmptyList_ReturnsAllDeletedWithZeroCount()
    {
        var handler = new BulkDeleteAssignmentsHandler(_db, _currentUser, _auth, _audit);
        var result  = await handler.Handle(new BulkDeleteAssignmentsCommand([]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AllDeleted.Should().BeTrue();
        result.Value.DeletedCount.Should().Be(0);
    }

    // ── BulkActivateAssignmentsHandler ────────────────────────────────────────

    [Fact]
    public async Task BulkActivate_ActivatesDeactivatedAssignments()
    {
        var payeeId = Guid.NewGuid();
        var planId  = Guid.NewGuid();
        var a1 = SeedAssignment(payeeId, planId, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var a2 = SeedAssignment(payeeId, planId, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));

        // Deactivate both
        a1.Deactivate("system", Now, Guid.NewGuid());
        a2.Deactivate("system", Now, Guid.NewGuid());
        await _db.SaveChangesAsync();

        var handler = new BulkActivateAssignmentsHandler(_db, _currentUser, _clock, _guid, _auth, _audit);
        var result  = await handler.Handle(new BulkActivateAssignmentsCommand([a1.Id, a2.Id]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Affected.Should().Be(2);
        _db.PlanAssignments.All(a => a.Status == AssignmentStatus.Active).Should().BeTrue();
    }

    [Fact]
    public async Task BulkActivate_AlreadyActiveAssignment_CountsAsError()
    {
        var payeeId = Guid.NewGuid();
        var planId  = Guid.NewGuid();
        var a1 = SeedAssignment(payeeId, planId, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        // a1 is already Active

        var handler = new BulkActivateAssignmentsHandler(_db, _currentUser, _clock, _guid, _auth, _audit);
        var result  = await handler.Handle(new BulkActivateAssignmentsCommand([a1.Id]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Affected.Should().Be(0);
        result.Value.Errors.Should().HaveCount(1);
    }

    // ── BulkDeactivateAssignmentsHandler ─────────────────────────────────────

    [Fact]
    public async Task BulkDeactivate_DeactivatesActiveAssignments()
    {
        var payeeId = Guid.NewGuid();
        var planId  = Guid.NewGuid();
        var a1 = SeedAssignment(payeeId, planId, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var a2 = SeedAssignment(payeeId, planId, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));

        var handler = new BulkDeactivateAssignmentsHandler(_db, _currentUser, _clock, _guid, _auth, _audit);
        var result  = await handler.Handle(new BulkDeactivateAssignmentsCommand([a1.Id, a2.Id]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Affected.Should().Be(2);
        _db.PlanAssignments.All(a => a.Status == AssignmentStatus.Deactivated).Should().BeTrue();
    }

    [Fact]
    public async Task BulkDeactivate_AlreadyDeactivatedAssignment_CountsAsError()
    {
        var payeeId = Guid.NewGuid();
        var planId  = Guid.NewGuid();
        var a1 = SeedAssignment(payeeId, planId, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        a1.Deactivate("system", Now, Guid.NewGuid());
        await _db.SaveChangesAsync();

        var handler = new BulkDeactivateAssignmentsHandler(_db, _currentUser, _clock, _guid, _auth, _audit);
        var result  = await handler.Handle(new BulkDeactivateAssignmentsCommand([a1.Id]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Affected.Should().Be(0);
        result.Value.Errors.Should().HaveCount(1);
    }

    // ── Period overlap edge cases ─────────────────────────────────────────────

    [Fact]
    public async Task BulkDelete_PayoutJustTouchingAssignmentBoundary_IsBlocked()
    {
        var payeeId = Guid.NewGuid();
        var planId  = Guid.NewGuid();
        // Assignment: Jan-Jun. Payout ends exactly on Jan 1 → overlaps at single day.
        var a1 = SeedAssignment(payeeId, planId, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        SeedPayout(payeeId, planId, new DateOnly(2024, 12, 1), new DateOnly(2025, 1, 1));

        var handler = new BulkDeleteAssignmentsHandler(_db, _currentUser, _auth, _audit);
        var result  = await handler.Handle(new BulkDeleteAssignmentsCommand([a1.Id]), CancellationToken.None);

        result.Value!.AllDeleted.Should().BeFalse();
        result.Value.Blocked.Should().HaveCount(1);
    }

    [Fact]
    public async Task BulkDelete_PayoutBeforeAssignmentStart_IsNotBlocked()
    {
        var payeeId = Guid.NewGuid();
        var planId  = Guid.NewGuid();
        var a1 = SeedAssignment(payeeId, planId, new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31));
        // Payout ends before assignment starts — no overlap
        SeedPayout(payeeId, planId, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));

        var handler = new BulkDeleteAssignmentsHandler(_db, _currentUser, _auth, _audit);
        var result  = await handler.Handle(new BulkDeleteAssignmentsCommand([a1.Id]), CancellationToken.None);

        result.Value!.AllDeleted.Should().BeTrue();
        _db.PlanAssignments.Count().Should().Be(0);
    }
}
