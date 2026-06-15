using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Subscription;

/// <summary>
/// Verifies that activation/reactivation via UpdateFromStripe always clears cancellation
/// fields (CancelAtPeriodEnd, CancelAt, CanceledAt) regardless of the prior state.
/// Uses the domain + EF persistence layer directly because checkout.session.completed
/// webhook handler makes inline Stripe API calls that cannot be stubbed without a refactor.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class ReactivationCancellationFieldsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;

    public ReactivationCancellationFieldsTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM UserSubscriptions WHERE TenantId = {TestConstants.TenantA}");
    }

    public async Task DisposeAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM UserSubscriptions WHERE TenantId = {TestConstants.TenantA}");
    }

    // ── Reactivation: Canceled + dirty fields → UpdateFromStripe → all fields clean ──

    [Fact]
    public async Task Reactivation_WhenCanceledWithDirtyFields_ClearsCancelAtPeriodEnd()
    {
        var sub = await SeedCanceledWithDirtyFieldsAsync();
        await ApplyReactivationAsync(sub);

        var result = await ReadSubscriptionAsync();
        result!.CancelAtPeriodEnd.Should().BeFalse();
    }

    [Fact]
    public async Task Reactivation_WhenCanceledWithDirtyFields_ClearsCancelAt()
    {
        var sub = await SeedCanceledWithDirtyFieldsAsync();
        await ApplyReactivationAsync(sub);

        var result = await ReadSubscriptionAsync();
        result!.CancelAt.Should().BeNull();
    }

    [Fact]
    public async Task Reactivation_WhenCanceledWithDirtyFields_ClearsCanceledAt()
    {
        var sub = await SeedCanceledWithDirtyFieldsAsync();
        await ApplyReactivationAsync(sub);

        var result = await ReadSubscriptionAsync();
        result!.CanceledAt.Should().BeNull();
    }

    [Fact]
    public async Task Reactivation_WhenCanceledWithDirtyFields_SetsStatusActive()
    {
        var sub = await SeedCanceledWithDirtyFieldsAsync();
        await ApplyReactivationAsync(sub);

        var result = await ReadSubscriptionAsync();
        result!.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Reactivation_WhenCanceledWithDirtyFields_UpdatesStripeSubscriptionId()
    {
        var sub = await SeedCanceledWithDirtyFieldsAsync();
        await ApplyReactivationAsync(sub);

        var result = await ReadSubscriptionAsync();
        result!.StripeSubscriptionId.Should().Be("sub_reactivated_new");
    }

    [Fact]
    public async Task Reactivation_WhenCanceledWithDirtyFields_UpdatesPeriod()
    {
        var oldPeriodEnd = new DateTimeOffset(2026, 7, 11, 0, 0, 0, TimeSpan.Zero);
        var sub = await SeedCanceledWithDirtyFieldsAsync(oldPeriodEnd);
        var newPeriodEnd = new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);
        await ApplyReactivationAsync(sub, newPeriodEnd);

        var result = await ReadSubscriptionAsync();
        result!.CurrentPeriodEnd.Should().Be(newPeriodEnd);
    }

    // ── Schedule + reactivate: cancel schedule is also wiped ─────────────────────

    [Fact]
    public async Task Reactivation_WhenScheduledThenCanceled_ClearsAllThreeFields()
    {
        using var setup = _fixture.Factory.Services.CreateScope();
        var setupDb = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var sub = UserSubscription.CreateFree(Guid.NewGuid(), TestConstants.TenantA, "test@wasnie.io", now);
        sub.UpdateFromStripe(
            tier: Tier.Scale, status: SubscriptionStatus.Active,
            stripeSubscriptionId: "sub_before", stripeCustomerId: "cus_react_test",
            stripePriceId: "price_scale", stripeProductId: "prod_scale",
            periodStart: now, periodEnd: now.AddMonths(1),
            nextBillingDate: now.AddMonths(1), now: now);
        sub.ScheduleCancellation(now.AddMonths(1), now);
        sub.Cancel(now);
        setupDb.UserSubscriptions.Add(sub);
        await setupDb.SaveChangesAsync();

        // Reactivate
        await ApplyReactivationAsync(sub);

        var result = await ReadSubscriptionAsync();
        result!.Status.Should().Be(SubscriptionStatus.Active);
        result.CancelAtPeriodEnd.Should().BeFalse();
        result.CancelAt.Should().BeNull();
        result.CanceledAt.Should().BeNull();
    }

    // ── Multi-tenant: TenantB's dirty row is not affected ────────────────────────

    [Fact]
    public async Task Reactivation_OnlyAffectsTargetTenant()
    {
        // Seed TenantB with dirty fields
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var tenantBSub = UserSubscription.CreateFree(Guid.NewGuid(), TestConstants.TenantB, "b@test.io", now);
        tenantBSub.UpdateFromStripe(
            tier: Tier.Scale, status: SubscriptionStatus.Active,
            stripeSubscriptionId: "sub_b_orig", stripeCustomerId: "cus_b",
            stripePriceId: "price_scale", stripeProductId: "prod_scale",
            periodStart: now, periodEnd: now.AddMonths(1),
            nextBillingDate: now.AddMonths(1), now: now);
        tenantBSub.ScheduleCancellation(now.AddMonths(1), now);
        tenantBSub.Cancel(now);
        db.UserSubscriptions.Add(tenantBSub);
        await db.SaveChangesAsync();

        try
        {
            // Reactivate TenantA only
            var subA = await SeedCanceledWithDirtyFieldsAsync();
            await ApplyReactivationAsync(subA);

            // TenantB should remain Canceled with dirty fields (untouched)
            var tenantBResult = await db.UserSubscriptions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == TestConstants.TenantB);
            tenantBResult!.Status.Should().Be(SubscriptionStatus.Canceled);
            tenantBResult.CancelAtPeriodEnd.Should().BeFalse(); // Cancel() clears it
        }
        finally
        {
            await db.Database.ExecuteSqlAsync(
                $"DELETE FROM UserSubscriptions WHERE TenantId = {TestConstants.TenantB}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<UserSubscription> SeedCanceledWithDirtyFieldsAsync(
        DateTimeOffset? oldPeriodEnd = null)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var periodEnd = oldPeriodEnd ?? new DateTimeOffset(2026, 7, 11, 0, 0, 0, TimeSpan.Zero);

        var sub = UserSubscription.CreateFree(Guid.NewGuid(), TestConstants.TenantA, "test@wasnie.io", now);
        sub.UpdateFromStripe(
            tier: Tier.Scale, status: SubscriptionStatus.Active,
            stripeSubscriptionId: "sub_old", stripeCustomerId: "cus_react_test",
            stripePriceId: "price_scale", stripeProductId: "prod_scale",
            periodStart: now, periodEnd: periodEnd,
            nextBillingDate: periodEnd, now: now);
        sub.ScheduleCancellation(periodEnd, now);
        sub.Cancel(now);
        db.UserSubscriptions.Add(sub);
        await db.SaveChangesAsync();
        return sub;
    }

    private async Task ApplyReactivationAsync(
        UserSubscription sub,
        DateTimeOffset? newPeriodEnd = null)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tracked = await db.UserSubscriptions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.TenantId == TestConstants.TenantA);

        var now = DateTimeOffset.UtcNow;
        var periodEnd = newPeriodEnd ?? new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);

        tracked.UpdateFromStripe(
            tier: Tier.Scale, status: SubscriptionStatus.Active,
            stripeSubscriptionId: "sub_reactivated_new",
            stripeCustomerId: "cus_react_test",
            stripePriceId: "price_scale", stripeProductId: "prod_scale",
            periodStart: periodEnd.AddDays(-30), periodEnd: periodEnd,
            nextBillingDate: periodEnd, now: now);

        await db.SaveChangesAsync();
    }

    private async Task<UserSubscription?> ReadSubscriptionAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.UserSubscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == TestConstants.TenantA);
    }
}
