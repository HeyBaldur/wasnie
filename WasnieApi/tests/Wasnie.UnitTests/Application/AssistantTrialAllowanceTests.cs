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
        Build(AccountAccessState state, long limit)
    {
        var tenantId = Guid.NewGuid();
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            ctx, Substitute.For<MediatR.IPublisher>());

        var entitlement = new AssistantEntitlement(
            Substitute.For<IClaimsService>(), new FakePaidPlanGate(), new FakeAccountAccessReader(state), db, ctx,
            Options.Create(new BillingOptions { TrialAssistantTokenLimit = limit }));
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

        (await entitlement.IsTrialAllowanceExhaustedAsync()).Should().BeFalse("999 of 1,000 tokens used");
    }

    [Fact]
    public async Task Input_and_output_together_reach_the_allowance()
    {
        var (entitlement, db, tenantId) = Build(AccountAccessState.Trial, limit: 1_000);
        SeedUsage(db, tenantId, prompt: 700, completion: 100);
        SeedUsage(db, tenantId, prompt: 150, completion: 50);

        (await entitlement.IsTrialAllowanceExhaustedAsync()).Should().BeTrue("800 + 200 = 1,000: the limit is the combined total");
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

        (await entitlement.IsTrialAllowanceExhaustedAsync()).Should().BeFalse(
            "400 questions with no tokens recorded spent nothing — the old 300-message cut is gone");
    }

    [Fact]
    public async Task A_call_with_no_reported_usage_adds_nothing_but_is_not_invented()
    {
        var (entitlement, db, tenantId) = Build(AccountAccessState.Trial, limit: 1_000);
        SeedUsage(db, tenantId, prompt: null, completion: null);

        (await entitlement.IsTrialAllowanceExhaustedAsync()).Should().BeFalse();
        (await AssistantTokenMeter.UsedAsync(db, tenantId, null, CancellationToken.None)).Should().Be(0);
        db.AssistantTokenUsages.Should().ContainSingle("the call is still on record, with null tokens");
    }

    [Fact]
    public async Task A_paying_account_has_no_allowance_at_all()
    {
        var (entitlement, db, tenantId) = Build(AccountAccessState.Active, limit: 1_000);
        SeedUsage(db, tenantId, prompt: 50_000, completion: 50_000);

        (await entitlement.IsTrialAllowanceExhaustedAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Other_tenants_tokens_do_not_count()
    {
        var (entitlement, db, _) = Build(AccountAccessState.Trial, limit: 1_000);
        SeedUsage(db, Guid.NewGuid(), prompt: 5_000, completion: 5_000);

        (await entitlement.IsTrialAllowanceExhaustedAsync()).Should().BeFalse();
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
