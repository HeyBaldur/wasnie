using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Entities;
using Wasnie.Infrastructure.Identity;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Integrations;

/// <summary>
/// The gate that decides whether a tenant's plan includes the metered capabilities (AI assistant,
/// HubSpot). Two things are pinned here and both cost money if they break:
/// the tier is read from the DATABASE (so a downgrade takes effect immediately, not when a token
/// expires), and anything short of a proven paid tenant is a refusal.
/// </summary>
public sealed class PaidPlanGateTests
{
    private static ApplicationDbContext NewDb(ITenantContext? ctx = null) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"paid-plan-gate-{Guid.NewGuid()}")
                .Options,
            ctx ?? Substitute.For<ITenantContext>(), Substitute.For<MediatR.IPublisher>());

    private static (PaidPlanGate Gate, ApplicationDbContext Db, Guid TenantId) Build(Tier tier)
    {
        var tenantId = Guid.NewGuid();

        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);

        var db = NewDb(ctx);

        var tenant = Tenant.Create($"Tenant {tier}", $"{tier}-{tenantId:N}", tenantId, DateTimeOffset.UtcNow);
        tenant.SetTier(tier);
        db.Tenants.Add(tenant);
        db.SaveChanges();

        return (new PaidPlanGate(db, ctx), db, tenantId);
    }

    [Theory]
    [InlineData(Tier.Starter)]
    [InlineData(Tier.Growth)]
    [InlineData(Tier.Scale)]
    [InlineData(Tier.Enterprise)]
    public async Task Every_paid_tier_including_ones_added_later_passes(Tier tier)
    {
        var (gate, _, _) = Build(tier);

        (await gate.IsOnPaidPlanAsync()).Should().BeTrue();
        await gate.Invoking(g => g.RequirePaidPlanAsync("HubSpot")).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Free_is_refused_and_the_refusal_names_the_upgrade_target()
    {
        var (gate, _, _) = Build(Tier.Free);

        (await gate.IsOnPaidPlanAsync()).Should().BeFalse();

        var thrown = await gate.Invoking(g => g.RequirePaidPlanAsync("HubSpot"))
            .Should().ThrowAsync<PaidPlanRequiredException>();

        thrown.Which.Feature.Should().Be("HubSpot");
        thrown.Which.CurrentTier.Should().Be(nameof(Tier.Free));
        thrown.Which.UpgradeTier.Should().Be(nameof(Tier.Starter),
            "the client offers the cheapest tier that unlocks it, not the most expensive");
    }

    [Fact]
    public async Task A_downgrade_takes_effect_on_the_very_next_call()
    {
        // ★ The reason the tier is not a JWT claim. The token in this request was minted while the
        // tenant was paying; the plan changed underneath it. If this ever starts returning true, a
        // downgraded tenant keeps spending our HubSpot quota until their session expires.
        var (gate, db, tenantId) = Build(Tier.Growth);
        (await gate.IsOnPaidPlanAsync()).Should().BeTrue();

        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == tenantId);
        tenant.SetTier(Tier.Free);
        await db.SaveChangesAsync();

        (await gate.IsOnPaidPlanAsync()).Should().BeFalse("the plan is read fresh, never cached per-request");
    }

    [Fact]
    public async Task An_unresolved_tenant_is_refused_rather_than_assumed_paid()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);

        var gate = new PaidPlanGate(NewDb(), ctx);

        (await gate.IsOnPaidPlanAsync()).Should().BeFalse("\"we don't know\" must never spend money");
        await gate.Invoking(g => g.RequirePaidPlanAsync("HubSpot"))
            .Should().ThrowAsync<PaidPlanRequiredException>();
    }

    [Fact]
    public async Task A_tenant_row_that_does_not_exist_is_refused()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(Guid.NewGuid());
        ctx.IsResolved.Returns(true);

        var gate = new PaidPlanGate(NewDb(), ctx);

        (await gate.IsOnPaidPlanAsync()).Should().BeFalse();
    }
}
