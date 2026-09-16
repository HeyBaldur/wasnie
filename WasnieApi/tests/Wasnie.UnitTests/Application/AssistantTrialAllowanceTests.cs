using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Assistant;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Identity;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-77 / KAN-80 — a trial gets the assistant, with a configurable TOKEN allowance (input + output) as a safety net.
/// The allowance is tenant-wide (every user together) and never applies to paying accounts.
///
/// ★ WHAT CHANGED IN KAN-80. This used to count user MESSAGES against 300. A message costs anywhere from a few hundred to
/// tens of thousands of tokens, so the count measured nothing the provider bills. The tests below seed token rows and
/// no messages at all: a thread full of questions with no tokens spent must not exhaust anything.
/// </summary>
public sealed class AssistantTrialAllowanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static (AssistantEntitlement Entitlement, ApplicationDbContext Db, Guid TenantId)
        Build(AccountAccessState state, long limit, long includedPerMonth = 0)
    {
        var tenantId = Guid.NewGuid();
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            ctx, Substitute.For<MediatR.IPublisher>());

        var accessReader = new FakeAccountAccessReader(state);
        var options = Options.Create(new BillingOptions
        {
            TrialAssistantTokenLimit = limit,
            IncludedAssistantTokensPerMonth = includedPerMonth,
        });

        // ★ THE REAL BALANCE READER, not a stub. The refusal's whole job is to read the same number the meter shows;
        // a stubbed balance here would test the `if` and leave the wiring that actually breaks uncovered (§A2).
        var entitlement = new AssistantEntitlement(
            Substitute.For<IClaimsService>(), new FakePaidPlanGate(), accessReader,
            new AssistantTokenBalanceReader(db, accessReader, options, new FakeClock(Now.UtcDateTime)), ctx);
        return (entitlement, db, tenantId);
    }

    private static void SeedUsage(ApplicationDbContext db, Guid tenantId, int? prompt, int? completion, DateTimeOffset? at = null)
    {
        db.AssistantTokenUsages.Add(AssistantTokenUsage.Create(
            Guid.NewGuid(), tenantId, "user-a", Guid.NewGuid(), "Answer", "openai/gpt-oss-120b", "Cerebras",
            prompt, completion, null, null, at ?? Now));
        db.SaveChanges();
    }

    [Fact]
    public async Task A_trial_under_the_token_allowance_may_keep_talking()
    {
        var (entitlement, db, tenantId) = Build(AccountAccessState.Trial, limit: 1_000);
        SeedUsage(db, tenantId, prompt: 600, completion: 399);

        (await entitlement.TokenRefusalKeyAsync()).Should().BeNull("999 of 1,000 tokens used");
    }

    [Fact]
    public async Task Input_and_output_together_reach_the_allowance()
    {
        var (entitlement, db, tenantId) = Build(AccountAccessState.Trial, limit: 1_000);
        SeedUsage(db, tenantId, prompt: 700, completion: 100);
        SeedUsage(db, tenantId, prompt: 150, completion: 50);

        (await entitlement.TokenRefusalKeyAsync()).Should().Be(IAssistantEntitlement.TrialAllowanceExhaustedKey,
            "800 + 200 = 1,000: the limit is the combined total");
    }

    [Fact]
    public async Task Messages_no_longer_count_only_tokens_do()
    {
        var (entitlement, db, tenantId) = Build(AccountAccessState.Trial, limit: 1_000);
        var conversationId = Guid.NewGuid();
        for (var i = 0; i < 400; i++)
        {
            db.AssistantMessages.Add(AssistantMessage.Create(
                Guid.NewGuid(), conversationId, tenantId, AssistantMessageRole.User, $"q{i}", i, Now));
        }
        db.SaveChanges();

        (await entitlement.TokenRefusalKeyAsync()).Should().BeNull(
            "400 questions with no tokens recorded spent nothing — the old 300-message cut is gone");
    }

    [Fact]
    public async Task A_call_with_no_reported_usage_adds_nothing_but_is_not_invented()
    {
        var (entitlement, db, tenantId) = Build(AccountAccessState.Trial, limit: 1_000);
        SeedUsage(db, tenantId, prompt: null, completion: null);

        (await entitlement.TokenRefusalKeyAsync()).Should().BeNull();
        (await AssistantTokenMeter.UsedAsync(db, tenantId, null, CancellationToken.None)).Should().Be(0);
        db.AssistantTokenUsages.Should().ContainSingle("the call is still on record, with null tokens");
    }

    [Fact]
    public async Task A_paying_account_does_not_spend_the_trial_allowance()
    {
        // ★ THE TRIAL'S LIMIT IS NOT A CEILING FOR PAYING TENANTS. Before KAN-83 this meant they had no ceiling at
        // all; now they have their own (below), but it is never the trial's — a paying tenant blocked at 1,000 tokens
        // because the trial setting is small would be the worst kind of billing bug.
        var (entitlement, db, tenantId) = Build(AccountAccessState.Active, limit: 1_000, includedPerMonth: 3_000_000);
        SeedUsage(db, tenantId, prompt: 50_000, completion: 50_000);

        (await entitlement.TokenRefusalKeyAsync()).Should().BeNull();
    }

    [Fact]
    public async Task A_paying_tenant_out_of_its_included_tokens_is_told_to_buy_a_boost()
    {
        // KAN-83: the ceiling that lets the assistant be opened to every user of a flat-priced account.
        var (entitlement, db, tenantId) = Build(AccountAccessState.Active, limit: 1_000, includedPerMonth: 100_000);
        SeedUsage(db, tenantId, prompt: 60_000, completion: 40_000);

        (await entitlement.TokenRefusalKeyAsync()).Should().Be(IAssistantEntitlement.TokensExhaustedKey,
            "a customer who already pays must not be sent to the paywall (§C3)");
    }

    [Fact]
    public async Task A_boost_lets_a_paying_tenant_past_its_included_tokens()
    {
        var (entitlement, db, tenantId) = Build(AccountAccessState.Active, limit: 1_000, includedPerMonth: 100_000);
        SeedUsage(db, tenantId, prompt: 60_000, completion: 40_000);

        db.AssistantTokenBoosts.Add(AssistantTokenBoost.Create(
            Guid.NewGuid(), tenantId, "evt_boost_1", "prod_boost_3m", "cs_1",
            tokens: 3_000_000, purchasedAt: Now.AddDays(-1), expiresAt: Now.AddDays(364)));
        db.SaveChanges();

        (await entitlement.TokenRefusalKeyAsync()).Should().BeNull("the boost they bought is exactly what this is for");
    }

    [Fact]
    public async Task An_expired_boost_does_not_let_a_paying_tenant_past_the_ceiling()
    {
        var (entitlement, db, tenantId) = Build(AccountAccessState.Active, limit: 1_000, includedPerMonth: 100_000);
        SeedUsage(db, tenantId, prompt: 60_000, completion: 40_000);

        db.AssistantTokenBoosts.Add(AssistantTokenBoost.Create(
            Guid.NewGuid(), tenantId, "evt_boost_old", "prod_boost_3m", "cs_1",
            tokens: 3_000_000, purchasedAt: Now.AddDays(-400), expiresAt: Now.AddDays(-35)));
        db.SaveChanges();

        (await entitlement.TokenRefusalKeyAsync()).Should().Be(IAssistantEntitlement.TokensExhaustedKey);
    }

    [Fact]
    public async Task A_locked_account_is_refused_by_the_paywall_not_by_the_token_ceiling()
    {
        // A locked tenant has no balance to act on; RequireAsync is what stops it, and inventing a token refusal here
        // would tell someone to buy a boost when what they need is a subscription.
        var (entitlement, db, tenantId) = Build(AccountAccessState.Locked, limit: 1_000, includedPerMonth: 100_000);
        SeedUsage(db, tenantId, prompt: 500_000, completion: 500_000);

        (await entitlement.TokenRefusalKeyAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Other_tenants_tokens_do_not_count()
    {
        var (entitlement, db, _) = Build(AccountAccessState.Trial, limit: 1_000);
        SeedUsage(db, Guid.NewGuid(), prompt: 5_000, completion: 5_000);

        (await entitlement.TokenRefusalKeyAsync()).Should().BeNull();
    }

    [Fact]
    public async Task The_meter_counts_from_a_date_when_asked_for_a_billing_period()
    {
        var (_, db, tenantId) = Build(AccountAccessState.Active, limit: 1_000);
        SeedUsage(db, tenantId, prompt: 1_000, completion: 0, at: Now.AddDays(-40));
        SeedUsage(db, tenantId, prompt: 300, completion: 20, at: Now.AddDays(-2));

        (await AssistantTokenMeter.UsedAsync(db, tenantId, since: Now.AddDays(-10), CancellationToken.None))
            .Should().Be(320, "only the current period");
        (await AssistantTokenMeter.UsedAsync(db, tenantId, since: null, CancellationToken.None))
            .Should().Be(1_320);
    }
}
