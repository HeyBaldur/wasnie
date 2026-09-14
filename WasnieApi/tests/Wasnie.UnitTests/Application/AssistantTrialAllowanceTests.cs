using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Assistant;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Identity;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-77 — a trial gets the assistant, with a configurable message allowance as a safety net against abuse.
/// The allowance is tenant-wide (every user together) and never applies to paying accounts.
/// </summary>
public sealed class AssistantTrialAllowanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static (AssistantEntitlement Entitlement, ApplicationDbContext Db, Guid TenantId, FakeAccountAccessReader Reader)
        Build(AccountAccessState state, int limit)
    {
        var tenantId = Guid.NewGuid();
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            ctx, Substitute.For<MediatR.IPublisher>());

        var reader = new FakeAccountAccessReader(state);
        var entitlement = new AssistantEntitlement(
            Substitute.For<IClaimsService>(), new FakePaidPlanGate(), reader, db, ctx,
            Options.Create(new BillingOptions { TrialAssistantMessageLimit = limit }));
        return (entitlement, db, tenantId, reader);
    }

    private static void SeedMessages(ApplicationDbContext db, Guid tenantId, int userTurns, int assistantTurns = 0)
    {
        var conversationId = Guid.NewGuid();
        var sequence = 0;
        for (var i = 0; i < userTurns; i++)
            db.AssistantMessages.Add(AssistantMessage.Create(
                Guid.NewGuid(), conversationId, tenantId, AssistantMessageRole.User, $"q{i}", sequence++, Now));
        for (var i = 0; i < assistantTurns; i++)
            db.AssistantMessages.Add(AssistantMessage.Create(
                Guid.NewGuid(), conversationId, tenantId, AssistantMessageRole.Assistant, $"a{i}", sequence++, Now));
        db.SaveChanges();
    }

    [Fact]
    public async Task A_trial_under_the_allowance_may_keep_talking()
    {
        var (entitlement, db, tenantId, _) = Build(AccountAccessState.Trial, limit: 3);
        SeedMessages(db, tenantId, userTurns: 2, assistantTurns: 5);

        (await entitlement.IsTrialAllowanceExhaustedAsync()).Should().BeFalse(
            "only the USER turns count — the answers are not requests");
    }

    [Fact]
    public async Task A_trial_at_the_allowance_is_refused_a_new_turn()
    {
        var (entitlement, db, tenantId, _) = Build(AccountAccessState.Trial, limit: 3);
        SeedMessages(db, tenantId, userTurns: 3);

        (await entitlement.IsTrialAllowanceExhaustedAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task A_paying_account_has_no_allowance_at_all()
    {
        var (entitlement, db, tenantId, _) = Build(AccountAccessState.Active, limit: 3);
        SeedMessages(db, tenantId, userTurns: 50);

        (await entitlement.IsTrialAllowanceExhaustedAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Other_tenants_messages_do_not_count()
    {
        var (entitlement, db, _, _) = Build(AccountAccessState.Trial, limit: 3);
        SeedMessages(db, Guid.NewGuid(), userTurns: 10);

        (await entitlement.IsTrialAllowanceExhaustedAsync()).Should().BeFalse();
    }
}
