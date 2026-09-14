using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Helpers;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Subscription;

/// <summary>
/// KAN-77 end to end, on SQL Server: the trial is granted at registration from configuration, an ended trial
/// hits the paywall (and the paywall does not delete anything), and paying removes it on the next request.
///
/// ★ Uses TenantB and puts it back afterwards: the shared fixture seeds each tenant once, so a trial end date
/// left in the past would lock TenantB for every test that runs later.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class TrialAccessTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;

    public TrialAccessTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlAsync($"DELETE FROM UserSubscriptions WHERE TenantId = {TestConstants.TenantB}");
            var restored = DateTimeOffset.UtcNow.AddYears(10);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE Tenants SET TrialEndsAt = {restored} WHERE Id = {TestConstants.TenantB}");
        });
    }

    private async Task WithDbAsync(Func<ApplicationDbContext, Task> action)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task SetTrialEndAsync(DateTimeOffset endsAt) => WithDbAsync(db =>
        db.Database.ExecuteSqlAsync($"UPDATE Tenants SET TrialEndsAt = {endsAt} WHERE Id = {TestConstants.TenantB}"));

    private Task AddSubscriptionAsync(SubscriptionStatus status, bool stripe) => WithDbAsync(async db =>
    {
        await db.Database.ExecuteSqlAsync($"DELETE FROM UserSubscriptions WHERE TenantId = {TestConstants.TenantB}");
        var now = DateTimeOffset.UtcNow;
        var sub = UserSubscription.CreatePending(Guid.NewGuid(), TestConstants.TenantB, "b@wasnie.io", now);
        if (stripe)
            sub.UpdateFromStripe("pro", status, "sub_trial_test", "cus_trial_test", "price_299", "prod_299",
                now, now.AddMonths(1), now.AddMonths(1), now);
        else if (status == SubscriptionStatus.Active)
            sub.Recover(now); // the legacy free row: Active, no Stripe id
        db.UserSubscriptions.Add(sub);
        await db.SaveChangesAsync();
    });

    [Fact]
    public async Task Registration_starts_a_trial_of_the_configured_length()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var before = DateTimeOffset.UtcNow;
        var response = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/register-tenant", new
        {
            TenantName = $"Trial Tenant {suffix}",
            TenantSlug = $"trial-{suffix}",
            AdminEmail = $"trial-{suffix}@test.com",
            AdminPassword = "TestPassword!1",
            AdminFirstName = "Trial",
            AdminLastName = "User",
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await WithDbAsync(async db =>
        {
            var trialEndsAt = await db.Tenants.IgnoreQueryFilters()
                .Where(t => t.Slug == $"trial-{suffix}").Select(t => t.TrialEndsAt).SingleAsync();

            trialEndsAt.Should().NotBeNull();
            trialEndsAt!.Value.Should().BeCloseTo(before.AddDays(7), TimeSpan.FromMinutes(2),
                "Billing:TrialDays is 7 in the shipped configuration");
        });
    }

    [Fact]
    public async Task An_ended_trial_hits_the_paywall_but_can_still_read_why()
    {
        await SetTrialEndAsync(DateTimeOffset.UtcNow.AddMinutes(-1));
        var client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);

        var blocked = await client.GetAsync("/api/payees");
        blocked.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        var body = await blocked.Content.ReadFromJsonAsync<LockedBody>();
        body!.Code.Should().Be("account_locked");
        body.Reason.Should().Be("TrialEnded");

        var access = await client.GetAsync("/api/subscription/access");
        access.StatusCode.Should().Be(HttpStatusCode.OK, "the paywall screen needs this endpoint to say why");
        var dto = await access.Content.ReadFromJsonAsync<AccessBody>();
        dto!.State.Should().Be("Locked");
        dto.LockReason.Should().Be("TrialEnded");
    }

    [Fact]
    public async Task A_former_free_plan_row_marked_Active_does_not_open_the_paywall()
    {
        // ★★ The old select-free wrote Status = Active with no Stripe id. That is not paying.
        await SetTrialEndAsync(DateTimeOffset.UtcNow.AddMinutes(-1));
        await AddSubscriptionAsync(SubscriptionStatus.Active, stripe: false);

        var response = await _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB).GetAsync("/api/payees");

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task Paying_lifts_the_paywall_on_the_next_request_with_the_data_intact()
    {
        var client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
        var created = await client.PostAsJsonAsync("/api/payees", new
        {
            fullName = "Kept Through The Paywall",
            employeeCode = $"KEEP{Guid.NewGuid().ToString("N")[..6]}",
            email = $"keep-{Guid.NewGuid():N}@test.com",
            hireDate = "2024-01-01",
        });
        created.EnsureSuccessStatusCode();

        await SetTrialEndAsync(DateTimeOffset.UtcNow.AddMinutes(-1));
        (await client.GetAsync("/api/payees")).StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

        await AddSubscriptionAsync(SubscriptionStatus.Active, stripe: true);

        var after = await client.GetAsync("/api/payees?search=Kept%20Through");
        after.StatusCode.Should().Be(HttpStatusCode.OK);
        (await after.Content.ReadAsStringAsync()).Should().Contain("Kept Through The Paywall",
            "the paywall blocks access, it never deletes");
    }

    [Fact]
    public async Task An_open_trial_reports_the_days_left()
    {
        await SetTrialEndAsync(DateTimeOffset.UtcNow.AddDays(3).AddHours(1));

        var dto = await (await _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB)
            .GetAsync("/api/subscription/access")).Content.ReadFromJsonAsync<AccessBody>();

        dto!.State.Should().Be("Trial");
        dto.TrialDaysRemaining.Should().Be(4, "days are rounded UP — 3 days and an hour is shown as 4");
        dto.AssistantTrialMessageLimit.Should().Be(300);
    }

    private sealed record LockedBody(string Code, string? Reason);

    private sealed record AccessBody(
        string State, string? LockReason, DateTimeOffset? TrialEndsAt, int? TrialDaysRemaining,
        int? AssistantTrialMessagesUsed, int? AssistantTrialMessageLimit);
}
