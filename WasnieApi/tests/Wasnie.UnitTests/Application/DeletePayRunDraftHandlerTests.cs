using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Application.Compensation.Handlers.PayRuns;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

public sealed class DeletePayRunDraftHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _auth;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly DeletePayRunDraftHandler _handler;

    public DeletePayRunDraftHandlerTests()
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

        _handler = new DeletePayRunDraftHandler(_db, _auth, _currentUser, _audit);
    }

    public void Dispose() => _db.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private PayRun SeedDraft(DateOnly start, DateOnly end)
    {
        var run = PayRun.Open(TenantId, start, end, "test", Guid.NewGuid(), Now);
        _db.PayRuns.Add(run);
        _db.SaveChanges();
        return run;
    }

    private PayRun SeedApproved(DateOnly start, DateOnly end)
    {
        var run = PayRun.Open(TenantId, start, end, "test", Guid.NewGuid(), Now);
        run.Approve("test", Now, Guid.NewGuid());
        _db.PayRuns.Add(run);
        _db.SaveChanges();
        return run;
    }

    private PayRun SeedPaid(DateOnly start, DateOnly end)
    {
        var run = PayRun.Open(TenantId, start, end, "test", Guid.NewGuid(), Now);
        run.Approve("test", Now, Guid.NewGuid());
        run.MarkPaid("test", Now, Guid.NewGuid());
        _db.PayRuns.Add(run);
        _db.SaveChanges();
        return run;
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteDraft_Succeeds_RunRemovedFromDb()
    {
        var run = SeedDraft(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var result = await _handler.Handle(new DeletePayRunDraftCommand(run.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var remaining = await _db.PayRuns.CountAsync();
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task DeleteDraft_Succeeds_AuditLogged()
    {
        var run = SeedDraft(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        await _handler.Handle(new DeletePayRunDraftCommand(run.Id), CancellationToken.None);

        await _audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e =>
                e.TenantId == TenantId &&
                e.Action == "PAY_RUN_DRAFT_DELETED" &&
                e.ResourceId == run.Id.ToString()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteApproved_ReturnsFailure_WithLockedMessage()
    {
        var run = SeedApproved(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var result = await _handler.Handle(new DeletePayRunDraftCommand(run.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("locked");
        // Run must still exist
        (await _db.PayRuns.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeletePaid_ReturnsFailure_WithLockedMessage()
    {
        var run = SeedPaid(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var result = await _handler.Handle(new DeletePayRunDraftCommand(run.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("locked");
        (await _db.PayRuns.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task NotFound_ReturnsFailure()
    {
        var result = await _handler.Handle(new DeletePayRunDraftCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task DeleteApproved_NoAuditLogged()
    {
        var run = SeedApproved(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        await _handler.Handle(new DeletePayRunDraftCommand(run.Id), CancellationToken.None);

        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteDraft_OtherRunsUntouched()
    {
        var draft = SeedDraft(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var approved = SeedApproved(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        await _handler.Handle(new DeletePayRunDraftCommand(draft.Id), CancellationToken.None);

        var remaining = await _db.PayRuns.ToListAsync();
        remaining.Should().ContainSingle(r => r.Id == approved.Id);
    }
}
