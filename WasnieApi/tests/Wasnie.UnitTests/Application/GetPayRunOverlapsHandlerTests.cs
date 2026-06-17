using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Handlers.PayRuns;
using Wasnie.Application.Compensation.Queries.PayRuns;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

public sealed class GetPayRunOverlapsHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _auth;
    private readonly GetPayRunOverlapsHandler _handler;

    public GetPayRunOverlapsHandlerTests()
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

        _handler = new GetPayRunOverlapsHandler(_db, _auth);
    }

    public void Dispose() => _db.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private PayRun SeedRun(DateOnly start, DateOnly end, PayRunStatus status)
    {
        var run = PayRun.Open(TenantId, start, end, "test", Guid.NewGuid(), Now);
        _db.PayRuns.Add(run);
        if (status == PayRunStatus.Approved)
            run.Approve("test", Now, Guid.NewGuid());
        else if (status == PayRunStatus.Paid)
        {
            run.Approve("test", Now, Guid.NewGuid());
            run.MarkPaid("test", Now, Guid.NewGuid());
        }
        _db.SaveChanges();
        return run;
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoOtherRuns_ReturnsEmptyList()
    {
        var target = SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), PayRunStatus.Draft);

        var result = await _handler.Handle(new GetPayRunOverlapsQuery(target.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task SamePeriod_Approved_ReturnsOverlap()
    {
        var approved = SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), PayRunStatus.Approved);
        var target   = SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), PayRunStatus.Draft);

        var result = await _handler.Handle(new GetPayRunOverlapsQuery(target.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(r => r.Id == approved.Id);
    }

    [Fact]
    public async Task SamePeriod_Paid_ReturnsOverlap()
    {
        var paid   = SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), PayRunStatus.Paid);
        var target = SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), PayRunStatus.Draft);

        var result = await _handler.Handle(new GetPayRunOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().ContainSingle(r => r.Id == paid.Id);
    }

    [Fact]
    public async Task DraftOverlap_NotIncluded()
    {
        // A Draft that overlaps should NOT appear — only Approved/Paid represent payment risk.
        SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), PayRunStatus.Draft);
        var target = SeedRun(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 20), PayRunStatus.Draft);

        var result = await _handler.Handle(new GetPayRunOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task PartialOverlap_StartInsideOther_ReturnsOverlap()
    {
        // Jun 1-20 approved, target is Jun 15-30 → overlap Jun 15-20
        var approved = SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), PayRunStatus.Approved);
        var target   = SeedRun(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 30), PayRunStatus.Draft);

        var result = await _handler.Handle(new GetPayRunOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().ContainSingle(r => r.Id == approved.Id);
    }

    [Fact]
    public async Task EndpointShared_CountsAsOverlap()
    {
        // Jun 1-15 approved, target Jun 15-30 — share exactly Jun 15.
        var approved = SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15), PayRunStatus.Approved);
        var target   = SeedRun(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 30), PayRunStatus.Draft);

        var result = await _handler.Handle(new GetPayRunOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().ContainSingle(r => r.Id == approved.Id);
    }

    [Fact]
    public async Task NoSharedDay_AdjacentPeriods_NoOverlap()
    {
        // Jun 1-14 approved, target Jun 15-30 — gap between them.
        SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 14), PayRunStatus.Approved);
        var target = SeedRun(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 30), PayRunStatus.Draft);

        var result = await _handler.Handle(new GetPayRunOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task PeriodBeforeTarget_NoOverlap()
    {
        SeedRun(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), PayRunStatus.Approved);
        var target = SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), PayRunStatus.Draft);

        var result = await _handler.Handle(new GetPayRunOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task MultipleOverlaps_AllReturned()
    {
        // Jun 1-30, Jun 1-12, Jun 1-10 all overlap a Jun 1-30 target
        var r1 = SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), PayRunStatus.Approved);
        var r2 = SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 12), PayRunStatus.Paid);
        var r3 = SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 10), PayRunStatus.Approved);
        var target = SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), PayRunStatus.Draft);

        var result = await _handler.Handle(new GetPayRunOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().HaveCount(3);
        result.Value!.Select(r => r.Id).Should().BeEquivalentTo(new[] { r1.Id, r2.Id, r3.Id });
    }

    [Fact]
    public async Task PayRunNotFound_ReturnsFailure()
    {
        var result = await _handler.Handle(new GetPayRunOverlapsQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task SelfNotIncluded_EvenIfApproved()
    {
        // An Approved run should not flag itself as its own overlap.
        var run = SeedRun(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), PayRunStatus.Approved);

        var result = await _handler.Handle(new GetPayRunOverlapsQuery(run.Id), CancellationToken.None);

        result.Value!.Should().BeEmpty();
    }
}
