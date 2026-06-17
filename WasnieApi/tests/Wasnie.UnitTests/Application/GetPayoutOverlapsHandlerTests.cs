using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Handlers.Payouts;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

public sealed class GetPayoutOverlapsHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PayeeA   = Guid.NewGuid();
    private static readonly Guid PayeeB   = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _auth;
    private readonly GetPayoutOverlapsHandler _handler;

    public GetPayoutOverlapsHandlerTests()
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

        _handler = new GetPayoutOverlapsHandler(_db, _auth);
    }

    public void Dispose() => _db.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private Guid SeedPlan(string name = "Base Plan")
    {
        var plan = Plan.Create(
            TenantId, name, string.Empty,
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "EUR", "test", Guid.NewGuid(), Now, Guid.NewGuid());
        _db.CompensationPlans.Add(plan);
        _db.SaveChanges();
        return plan.Id;
    }

    private CompensationPayout SeedPayout(
        Guid payeeId,
        Guid planId,
        DateOnly start,
        DateOnly end,
        CompensationPayoutStatus status)
    {
        var snapshot = PayeeReference.Snapshot(payeeId, "Test Payee", "EMP-001");
        var period   = DateRange.Of(start, end);
        var spec     = new PayoutLineSpec(
            CreditId: Guid.NewGuid(),
            RuleId: Guid.NewGuid(),
            RuleName: "Base",
            BaseAmount: Money.Of(1000m, "EUR"),
            CommissionAmount: Money.Of(100m, "EUR"),
            AppliedModifiers: []);

        var payout = CompensationPayout.Calculate(
            TenantId, payeeId, planId, snapshot, period,
            [spec], "EUR", "test", Guid.NewGuid(), Now, Guid.NewGuid(),
            () => Guid.NewGuid());

        if (status == CompensationPayoutStatus.Approved ||
            status == CompensationPayoutStatus.Paid)
            payout.Approve("test", Now, Guid.NewGuid());

        if (status == CompensationPayoutStatus.Paid)
            payout.MarkPaid("test", Now);

        _db.CompensationPayouts.Add(payout);
        _db.SaveChanges();
        return payout;
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoOtherPayouts_ReturnsEmpty()
    {
        var planId = SeedPlan();
        var target = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Calculated);

        var result = await _handler.Handle(new GetPayoutOverlapsQuery(target.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task SamePeriod_ApprovedSamePayee_ReturnsOverlap()
    {
        var planId   = SeedPlan();
        var existing = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Approved);
        var target   = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Calculated);

        var result = await _handler.Handle(new GetPayoutOverlapsQuery(target.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(r => r.Id == existing.Id);
    }

    [Fact]
    public async Task SamePeriod_PaidSamePayee_ReturnsOverlap()
    {
        var planId   = SeedPlan();
        var existing = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Paid);
        var target   = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Calculated);

        var result = await _handler.Handle(new GetPayoutOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().ContainSingle(r => r.Id == existing.Id);
    }

    [Fact]
    public async Task SamePeriod_DifferentPayee_NotReturned()
    {
        var planId = SeedPlan();
        SeedPayout(PayeeB, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Approved);
        var target = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Calculated);

        var result = await _handler.Handle(new GetPayoutOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculatedOverlap_SamePayee_NotReturned()
    {
        // Only Approved/Paid create payment-risk — Calculated payouts must not appear.
        var planId = SeedPlan();
        SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Calculated);
        var target = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Calculated);

        var result = await _handler.Handle(new GetPayoutOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task PartialOverlap_ReturnsOverlap()
    {
        // Jun 1-20 approved, target Jun 15-30 → overlap Jun 15-20
        var planId   = SeedPlan();
        var existing = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20),
            CompensationPayoutStatus.Approved);
        var target   = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Calculated);

        var result = await _handler.Handle(new GetPayoutOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().ContainSingle(r => r.Id == existing.Id);
    }

    [Fact]
    public async Task SharedEndpoint_CountsAsOverlap()
    {
        // Jun 1-15 approved, target Jun 15-30 — share exactly Jun 15.
        var planId   = SeedPlan();
        var existing = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15),
            CompensationPayoutStatus.Approved);
        var target   = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Calculated);

        var result = await _handler.Handle(new GetPayoutOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().ContainSingle(r => r.Id == existing.Id);
    }

    [Fact]
    public async Task AdjacentPeriods_NoSharedDay_ReturnsEmpty()
    {
        // Jun 1-14 approved, target Jun 15-30 — no shared day.
        var planId = SeedPlan();
        SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 14),
            CompensationPayoutStatus.Approved);
        var target = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Calculated);

        var result = await _handler.Handle(new GetPayoutOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task PlanNameResolved_ReturnedInDto()
    {
        var planId   = SeedPlan("Enterprise Plan");
        var existing = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Approved);
        var target   = SeedPayout(PayeeA, planId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Calculated);

        var result = await _handler.Handle(new GetPayoutOverlapsQuery(target.Id), CancellationToken.None);

        result.Value!.Should().ContainSingle();
        result.Value![0].PlanName.Should().Be("Enterprise Plan");
    }

    [Fact]
    public async Task PayoutNotFound_ReturnsFailure()
    {
        var result = await _handler.Handle(new GetPayoutOverlapsQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }
}
