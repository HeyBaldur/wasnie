using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Commands;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.Handlers;
using Wasnie.Application.Assistant.Queries;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Assistant;
using Wasnie.Infrastructure.Identity;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The assistant's chat: persistence, ownership, and the fact that NO model is involved.
///
/// The load-bearing test in this file is the isolation one. Everything else here would still be true
/// of a chat that leaked across users; that one is the reason the feature can be private.
/// </summary>
public sealed class AssistantChatHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 9, 0, 0, TimeSpan.Zero);

    private const string AliceId = "user-alice";
    private const string BobId = "user-bob";

    /// <summary>
    /// One principal against one shared database. Tenant and user are both parameters because the
    /// isolation test needs three different principals looking at the same rows.
    /// </summary>
    private sealed record Principal(
        ApplicationDbContext Db,
        ICurrentUserService User,
        ITenantContext Tenant,
        FakeGuidGenerator Guids);

    private static ITenantContext TenantCtx(Guid tenantId)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        return ctx;
    }

    private static Principal As(string dbName, Guid tenantId, string userId)
    {
        var tenantCtx = TenantCtx(tenantId);

        // A context per principal, sharing the same InMemory store — which is exactly the situation
        // the isolation rule has to survive: same rows, different reader.
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(AssistantChatHandlerTests)}.{dbName}")
                .Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(userId);

        return new Principal(db, user, tenantCtx, new FakeGuidGenerator());
    }

    /// <summary>An entitlement that always says yes — the entitlement itself is tested separately.</summary>
    private static IAssistantEntitlement Entitled()
    {
        var e = Substitute.For<IAssistantEntitlement>();
        e.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        e.RequireAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return e;
    }

    private static StartConversationHandler Start(Principal p) =>
        new(p.Db, p.Tenant, p.User, new FakeClock(Now.UtcDateTime), p.Guids, Entitled());

    private static PostMessageHandler Post(Principal p) =>
        new(p.Db, p.Tenant, p.User, new FakeClock(Now.UtcDateTime), p.Guids, Entitled(),
            UnconfiguredProvider(), NoKnowledge(), NoNavigation(), NoRouter(), NoTools(), Options.Create(new GroqOptions()));

    /// <summary>
    /// No model configured, so these tests keep exercising the stand-in reply they were written
    /// against. The connected path has its own file (AssistantChatCompletionTests) — piece 1's
    /// guarantees (persistence, ownership, ordering) are the same either way.
    /// </summary>
    /// <summary>No tools either: no model is configured in these tests, so nothing looks anything up.</summary>
    private static AssistantToolRunner NoTools() =>
        new(UnconfiguredProvider(), [], NullLogger<AssistantToolRunner>.Instance);

    /// <summary>No routes either, for the same reason: nothing here guides anyone anywhere.</summary>
    private static IUiNavigationMap NoNavigation()
    {
        var navigation = Substitute.For<IUiNavigationMap>();
        navigation.PromptBlock.Returns(string.Empty);
        navigation.IsAvailable.Returns(false);
        navigation.Routes.Returns([]);
        return navigation;
    }

    /// <summary>No documentation: these tests are about persistence and ownership, not confinement.</summary>
    private static IAssistantKnowledgeBase NoKnowledge()
    {
        var knowledge = Substitute.For<IAssistantKnowledgeBase>();
        knowledge.Documentation.Returns(string.Empty);
        knowledge.IsAvailable.Returns(false);
        knowledge.Sections.Returns([]);
        knowledge.TableOfContents.Returns(string.Empty);
        knowledge.TextFor(Arg.Any<IEnumerable<string>>()).Returns(string.Empty);
        return knowledge;
    }

    /// <summary>With no documentation the router short-circuits and never calls the model.</summary>
    private static AssistantSectionRouter NoRouter() =>
        new(UnconfiguredProvider(), NoKnowledge(), NullLogger<AssistantSectionRouter>.Instance);

    private static IChatCompletionProvider UnconfiguredProvider()
    {
        var provider = Substitute.For<IChatCompletionProvider>();
        provider.IsConfigured.Returns(false);
        return provider;
    }

    private static GetConversationHandler Get(Principal p) => new(p.Db, p.User, Entitled());

    private static ListConversationsHandler List(Principal p) => new(p.Db, p.User, Entitled());

    // ── 1. Persistence with the right owner ───────────────────────────────────

    [Fact]
    public async Task A_conversation_and_its_messages_are_stored_against_the_right_tenant_and_user()
    {
        var tenantId = Guid.NewGuid();
        var alice = As(nameof(A_conversation_and_its_messages_are_stored_against_the_right_tenant_and_user), tenantId, AliceId);

        var started = await Start(alice).Handle(new StartConversationCommand("Q3 planning"), CancellationToken.None);
        started.IsSuccess.Should().BeTrue();

        var posted = await Post(alice).Handle(
            new PostMessageCommand(started.Value!.Id, "How do accelerators work?"), CancellationToken.None);
        posted.IsSuccess.Should().BeTrue();

        var conversation = await alice.Db.AssistantConversations.IgnoreQueryFilters().SingleAsync();
        conversation.TenantId.Should().Be(tenantId);
        conversation.UserId.Should().Be(AliceId);
        conversation.Title.Should().Be("Q3 planning");

        var messages = await alice.Db.AssistantMessages.IgnoreQueryFilters().ToListAsync();
        messages.Should().HaveCount(2);
        messages.Should().OnlyContain(m => m.TenantId == tenantId);
        messages.Should().OnlyContain(m => m.ConversationId == conversation.Id);

        // The thread's activity timestamp moved, which is what the history list sorts on.
        conversation.UpdatedAt.Should().Be(Now);
    }

    // ── 2. ★ THE ISOLATION TEST ───────────────────────────────────────────────

    [Fact]
    public async Task A_conversation_is_invisible_to_every_principal_except_its_owner()
    {
        // ★ THE TEST THIS FEATURE STANDS ON. Three principals, one store:
        //   - Alice, the owner
        //   - Bob, a COLLEAGUE in the SAME tenant   ← the case a tenant query filter does NOT cover
        //   - Carol, in another tenant
        // Bob is the one that matters. The global tenant filter would happily hand him Alice's chat;
        // only the UserId half of the filter stops it. Drop `.Where(c => c.UserId == ...)` from
        // OwnedConversations and this test goes red on Bob while every other test in the file passes.
        const string db = nameof(A_conversation_is_invisible_to_every_principal_except_its_owner);
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        var alice = As(db, tenantId, AliceId);
        var bob = As(db, tenantId, BobId);
        var carol = As(db, otherTenantId, "user-carol");

        var started = await Start(alice).Handle(new StartConversationCommand("Alice private"), CancellationToken.None);
        await Post(alice).Handle(new PostMessageCommand(started.Value!.Id, "my salary question"), CancellationToken.None);
        var conversationId = started.Value.Id;

        // The owner reads it.
        var byOwner = await Get(alice).Handle(new GetConversationQuery(conversationId), CancellationToken.None);
        byOwner.IsSuccess.Should().BeTrue();
        byOwner.Value!.Messages.Should().HaveCount(2);

        // ★ A colleague in the SAME tenant cannot.
        var byColleague = await Get(bob).Handle(new GetConversationQuery(conversationId), CancellationToken.None);
        byColleague.IsSuccess.Should().BeFalse("a user must not read another user's conversation");
        byColleague.Error.Should().Be("Conversation not found.",
            "the message must not reveal that the conversation exists");

        // Another tenant cannot either.
        var byOtherTenant = await Get(carol).Handle(new GetConversationQuery(conversationId), CancellationToken.None);
        byOtherTenant.IsSuccess.Should().BeFalse();

        // And it does not appear in their lists.
        (await List(bob).Handle(new ListConversationsQuery(), CancellationToken.None)).Value!.Should().BeEmpty();
        (await List(carol).Handle(new ListConversationsQuery(), CancellationToken.None)).Value!.Should().BeEmpty();
        (await List(alice).Handle(new ListConversationsQuery(), CancellationToken.None)).Value!.Should().ContainSingle();
    }

    [Fact]
    public async Task A_colleague_cannot_write_into_someone_elses_conversation()
    {
        // Reading is not the only leak: posting into another user's thread would put words in their
        // history. Same owned-query gate, asserted on the write path too.
        const string db = nameof(A_colleague_cannot_write_into_someone_elses_conversation);
        var tenantId = Guid.NewGuid();
        var alice = As(db, tenantId, AliceId);
        var bob = As(db, tenantId, BobId);

        var started = await Start(alice).Handle(new StartConversationCommand("Alice private"), CancellationToken.None);

        var byColleague = await Post(bob).Handle(
            new PostMessageCommand(started.Value!.Id, "injected"), CancellationToken.None);

        byColleague.IsSuccess.Should().BeFalse();
        (await alice.Db.AssistantMessages.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    // ── 3. The entitlement, tested through its own seam ───────────────────────

    [Fact]
    public async Task Only_an_entitled_user_reaches_the_chat_and_today_that_means_the_tenant_admin()
    {
        // ★ Written against the ENTITLEMENT, not against the role, so the day a seat is sold to a
        // CompManager this test is EXTENDED with a new case rather than rewritten: the question
        // ("is this principal entitled?") does not change, only the answer for a given principal.
        var claims = Substitute.For<IClaimsService>();
        var entitlement = new AssistantEntitlement(claims, new FakePaidPlanGate());

        claims.GetRole().Returns("TenantAdmin");
        (await entitlement.IsEnabledAsync()).Should().BeTrue("today the admin holds the only seat");
        await entitlement.Invoking(e => e.RequireAsync()).Should().NotThrowAsync();

        foreach (var role in new[] { "CompManager", "Manager", "Rep", null })
        {
            claims.GetRole().Returns(role);
            (await entitlement.IsEnabledAsync()).Should().BeFalse($"'{role ?? "no role"}' has no seat yet");
            await entitlement.Invoking(e => e.RequireAsync()).Should().ThrowAsync<ForbiddenException>();
        }
    }

    [Fact]
    public async Task The_plan_gates_the_assistant_and_the_seat_still_gates_it_independently()
    {
        // Two gates, two different refusals — the whole reason the UI can lock one and hide the other.
        var claims = Substitute.For<IClaimsService>();

        var adminOnFree = new AssistantEntitlement(claims, new FakePaidPlanGate(onPaidPlan: false));
        claims.GetRole().Returns("TenantAdmin");

        (await adminOnFree.IsEnabledAsync()).Should().BeFalse("Free does not include the assistant");
        (await adminOnFree.RequiresPaidPlanAsync()).Should().BeTrue(
            "the seat is held, so the plan is the ONLY thing missing — this is the locked-with-upgrade case");
        await adminOnFree.Invoking(e => e.RequireAsync())
            .Should().ThrowAsync<PaidPlanRequiredException>("the refusal must say it is billing, not authority");

        // A user with no seat is refused the same way on Free as on a paid plan, and never asked to upgrade:
        // buying a bigger plan would not give THEM the assistant, so offering it would be a lie.
        foreach (var gate in new[] { new FakePaidPlanGate(true), new FakePaidPlanGate(false) })
        {
            var seatless = new AssistantEntitlement(claims, gate);
            claims.GetRole().Returns("Rep");

            (await seatless.IsEnabledAsync()).Should().BeFalse();
            (await seatless.RequiresPaidPlanAsync()).Should().BeFalse("no upsell for someone a plan cannot help");
            await seatless.Invoking(e => e.RequireAsync()).Should().ThrowAsync<ForbiddenException>();
        }
    }

    [Fact]
    public async Task A_handler_refuses_a_user_without_the_entitlement()
    {
        // The gate is in the handler, not only in the UI — hiding the button is not access control.
        var alice = As(nameof(A_handler_refuses_a_user_without_the_entitlement), Guid.NewGuid(), AliceId);

        var denied = Substitute.For<IAssistantEntitlement>();
        denied.RequireAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw new ForbiddenException("Assistant.Use"));

        var handler = new StartConversationHandler(
            alice.Db, alice.Tenant, alice.User, new FakeClock(Now.UtcDateTime), alice.Guids, denied);

        await handler.Invoking(h => h.Handle(new StartConversationCommand(), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenException>();

        (await alice.Db.AssistantConversations.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    // ── 4. Reopening a conversation ───────────────────────────────────────────

    [Fact]
    public async Task Reopening_a_conversation_returns_its_messages_in_order()
    {
        var alice = As(nameof(Reopening_a_conversation_returns_its_messages_in_order), Guid.NewGuid(), AliceId);

        var started = await Start(alice).Handle(new StartConversationCommand("Ordered"), CancellationToken.None);
        var id = started.Value!.Id;

        await Post(alice).Handle(new PostMessageCommand(id, "first"), CancellationToken.None);
        await Post(alice).Handle(new PostMessageCommand(id, "second"), CancellationToken.None);

        var reopened = await Get(alice).Handle(new GetConversationQuery(id), CancellationToken.None);

        reopened.IsSuccess.Should().BeTrue();
        reopened.Value!.Messages.Select(m => m.Sequence).Should().Equal(0, 1, 2, 3);

        // ★ Ordered by Sequence, not by timestamp: all four turns share the same instant here, so a
        // CreatedAt sort would be free to interleave a question with the previous answer.
        reopened.Value.Messages.Select(m => m.Content).Should().Equal(
            "first",
            AssistantMessage.NotConnectedPlaceholder,
            "second",
            AssistantMessage.NotConnectedPlaceholder);

        reopened.Value.Messages.Select(m => m.Role).Should().Equal(
            "User", "Assistant", "User", "Assistant");
    }

    // ── 5. The reply is a stored placeholder, and no model was called ─────────

    [Fact]
    public async Task The_assistant_reply_is_a_persisted_placeholder_and_carries_no_payload()
    {
        var alice = As(nameof(The_assistant_reply_is_a_persisted_placeholder_and_carries_no_payload), Guid.NewGuid(), AliceId);

        var started = await Start(alice).Handle(new StartConversationCommand("Placeholder"), CancellationToken.None);
        var exchange = await Post(alice).Handle(
            new PostMessageCommand(started.Value!.Id, "anything"), CancellationToken.None);

        var reply = exchange.Value!.AssistantMessage;
        reply.Role.Should().Be("Assistant");
        reply.Content.Should().Be(AssistantMessage.NotConnectedPlaceholder);

        // ★ A SENTINEL, not English prose: the same row is read by a Spanish and a Polish user, and
        // the UI translates this marker. A stored sentence would freeze one language into history.
        reply.Content.Should().NotContain(" ", "the stored placeholder must not be a human sentence");

        // The reply really is in the database, not a client-side decoration.
        var stored = await alice.Db.AssistantMessages.IgnoreQueryFilters()
            .SingleAsync(m => m.Role == AssistantMessageRole.Assistant);
        stored.Content.Should().Be(AssistantMessage.NotConnectedPlaceholder);

        // ★ The structured payload exists and is EMPTY — reserved for later pieces (RAG references,
        // screen context, pre-fill JSON) and written by nothing today.
        stored.Payload.Should().BeNull();
        reply.Payload.Should().BeNull();
    }

    [Fact]
    public async Task The_history_list_is_sorted_by_most_recent_activity()
    {
        var alice = As(nameof(The_history_list_is_sorted_by_most_recent_activity), Guid.NewGuid(), AliceId);

        var older = await Start(alice).Handle(new StartConversationCommand("Older"), CancellationToken.None);
        var newer = await Start(alice).Handle(new StartConversationCommand("Newer"), CancellationToken.None);

        // Activity, not creation order: touching the older thread must float it to the top.
        var laterClock = new FakeClock(Now.AddHours(1).UtcDateTime);
        var post = new PostMessageHandler(
            alice.Db, alice.Tenant, alice.User, laterClock, alice.Guids, Entitled(),
            UnconfiguredProvider(), NoKnowledge(), NoNavigation(), NoRouter(), NoTools(), Options.Create(new GroqOptions()));
        await post.Handle(new PostMessageCommand(older.Value!.Id, "bump"), CancellationToken.None);

        var list = await List(alice).Handle(new ListConversationsQuery(), CancellationToken.None);

        list.Value!.Select(c => c.Title).Should().Equal("Older", "Newer");
        list.Value[0].MessageCount.Should().Be(2);
        list.Value[1].Id.Should().Be(newer.Value!.Id);
    }
}
