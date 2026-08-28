using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Commands;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.DTOs;
using Wasnie.Application.Assistant.Handlers;
using Wasnie.Application.Assistant.Queries;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Assistant;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Pinning a conversation, and the seam between the pinned group and the paged list.
///
/// ★★ THE PIN IS A ROW ON A (USER, CONVERSATION) TABLE, NOT A COLUMN ON THE CONVERSATION. Today a
/// conversation has exactly one owner, so a boolean on it would behave identically and be less code —
/// and it would be the wrong shape the day sharing arrives, which the feature is already designed to:
/// several people looking at one conversation, each with their own pins. Moving it then means migrating
/// live data instead of adding a row.
///
/// ★ AND THE HARD PART IS NOT THE PIN, IT IS THE PAGING. Pinned threads are exactly the OLD ones — the
/// ones that have sunk far below the first batch — so they are returned outside the cursor, complete.
/// Which immediately creates the opposite risk: the same conversation appearing twice, once in its
/// group and once in its time band. The exclusion tests are the ones that matter here.
/// </summary>
public sealed class AssistantPinTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    private const string AliceId = "user-alice";
    private const string BobId = "user-bob";

    private sealed record Principal(
        ApplicationDbContext Db, ICurrentUserService User, ITenantContext Tenant, Guid TenantId);

    private static Principal As(string dbName, Guid tenantId, string userId)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(AssistantPinTests)}.{dbName}")
                .Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(userId);

        return new Principal(db, user, tenantCtx, tenantId);
    }

    private static IAssistantEntitlement Entitled()
    {
        var e = Substitute.For<IAssistantEntitlement>();
        e.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        e.RequireAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return e;
    }

    private static PinConversationHandler Pin(Principal p, DateTimeOffset? at = null) =>
        new(p.Db, p.Tenant, p.User, new FakeClock((at ?? Now).UtcDateTime),
            new FakeGuidGenerator(), Entitled());

    private static UnpinConversationHandler Unpin(Principal p) =>
        new(p.Db, p.User, new FakeClock(Now.UtcDateTime), Entitled());

    private static ListConversationsHandler List(Principal p) => new(p.Db, p.User, Entitled());

    private static Task<AssistantConversationPageDto> PageAsync(
        Principal p, string? cursor = null, int? pageSize = null, string? search = null) =>
        List(p).Handle(new ListConversationsQuery(cursor, pageSize, search), CancellationToken.None)
            .ContinueWith(t => t.Result.Value!);

    private static Guid Seed(Principal p, string title, int minutesOld, string? userId = null)
    {
        var id = Guid.NewGuid();
        p.Db.AssistantConversations.Add(AssistantConversation.Start(
            id, p.TenantId, userId ?? p.User.UserId!, title, Now.AddMinutes(-minutesOld)));
        p.Db.SaveChanges();
        return id;
    }

    // ══ The row ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Pinning_creates_exactly_one_row_and_pinning_again_does_not_add_another()
    {
        var alice = As(nameof(Pinning_creates_exactly_one_row_and_pinning_again_does_not_add_another),
            Guid.NewGuid(), AliceId);
        var id = Seed(alice, "Q3", 5);

        (await Pin(alice).Handle(new PinConversationCommand(id), default)).IsSuccess.Should().BeTrue();
        (await Pin(alice).Handle(new PinConversationCommand(id), default)).IsSuccess.Should().BeTrue();

        var rows = await alice.Db.AssistantConversationStates.ToListAsync();
        rows.Should().ContainSingle();
        rows[0].IsPinned.Should().BeTrue();
    }

    [Fact]
    public async Task PINNING_SOMETHING_ALREADY_PINNED_DOES_NOT_MOVE_IT()
    {
        // ★ THE IDEMPOTENCE THAT IS VISIBLE. PinnedAt orders the pinned group, so rewriting it on a
        // second pin would make a double-click silently jump the conversation to the top of the pins.
        // The user asked for it to be pinned; it is pinned; nothing in that request says "and move it".
        var alice = As(nameof(PINNING_SOMETHING_ALREADY_PINNED_DOES_NOT_MOVE_IT), Guid.NewGuid(), AliceId);
        var id = Seed(alice, "Q3", 5);

        await Pin(alice).Handle(new PinConversationCommand(id), default);
        var first = (await alice.Db.AssistantConversationStates.SingleAsync()).PinnedAt;

        await Pin(alice, Now.AddHours(3)).Handle(new PinConversationCommand(id), default);

        (await alice.Db.AssistantConversationStates.SingleAsync()).PinnedAt.Should().Be(first);
    }

    [Fact]
    public async Task Unpinning_is_idempotent_and_KEEPS_THE_ROW()
    {
        // The row is this user's standing on the conversation, and the pin is only the first fact it
        // holds — "archived", "muted" and "last read" are the same shape and land on it.
        var alice = As(nameof(Unpinning_is_idempotent_and_KEEPS_THE_ROW), Guid.NewGuid(), AliceId);
        var id = Seed(alice, "Q3", 5);

        await Pin(alice).Handle(new PinConversationCommand(id), default);
        await Unpin(alice).Handle(new UnpinConversationCommand(id), default);
        (await Unpin(alice).Handle(new UnpinConversationCommand(id), default)).IsSuccess.Should().BeTrue();

        var row = await alice.Db.AssistantConversationStates.SingleAsync();
        row.IsPinned.Should().BeFalse();
        row.PinnedAt.Should().BeNull();
    }

    [Fact]
    public async Task Unpinning_something_never_pinned_succeeds_without_creating_a_row()
    {
        var alice = As(nameof(Unpinning_something_never_pinned_succeeds_without_creating_a_row),
            Guid.NewGuid(), AliceId);
        var id = Seed(alice, "Q3", 5);

        (await Unpin(alice).Handle(new UnpinConversationCommand(id), default)).IsSuccess.Should().BeTrue();

        (await alice.Db.AssistantConversationStates.CountAsync()).Should().Be(0);
    }

    // ══ ★ Authorisation ═══════════════════════════════════════════════════════

    [Fact]
    public async Task A_USER_CANNOT_PIN_ANOTHER_USERS_CONVERSATION_OR_ANOTHER_TENANTS()
    {
        // ★★ "A pin is my own preference, so pinning yours harms nobody" is the tempting reading, and it
        // is refused anyway. Writing a row keyed to an id I may not read is a way of ASKING whether that
        // id exists — pin it, and "did that succeed" answers the question. And the pinned group is built
        // by joining back to the conversations, so a pin on a thread I cannot read either leaks its
        // title or renders as nothing.
        var db = nameof(A_USER_CANNOT_PIN_ANOTHER_USERS_CONVERSATION_OR_ANOTHER_TENANTS);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var alice = As(db, tenantA, AliceId);
        var bob = As(db, tenantA, BobId);
        var carol = As(db, tenantB, AliceId);   // SAME user id, different tenant

        var bobsThread = Seed(bob, "Bob's private thread", 5);
        var otherTenantThread = Seed(carol, "Another tenant's thread", 5);

        var acrossUsers = await Pin(alice).Handle(new PinConversationCommand(bobsThread), default);
        var acrossTenants = await Pin(alice).Handle(new PinConversationCommand(otherTenantThread), default);

        acrossUsers.IsSuccess.Should().BeFalse();
        acrossTenants.IsSuccess.Should().BeFalse();

        // ★ AND THE SAME ANSWER FOR BOTH, plus for an id that never existed — "not yours", "another
        // tenant's" and "does not exist" must stay indistinguishable.
        acrossUsers.Error.Should().Be(OwnedConversations.NotFound);
        acrossTenants.Error.Should().Be(OwnedConversations.NotFound);
        (await Pin(alice).Handle(new PinConversationCommand(Guid.NewGuid()), default)).Error
            .Should().Be(OwnedConversations.NotFound);

        // Nothing was written for anybody.
        (await alice.Db.AssistantConversationStates.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_list_reports_the_ASKING_users_pins_not_somebody_elses()
    {
        var db = nameof(The_list_reports_the_ASKING_users_pins_not_somebody_elses);
        var tenantA = Guid.NewGuid();
        var alice = As(db, tenantA, AliceId);
        var bob = As(db, tenantA, BobId);

        var alicesThread = Seed(alice, "Alice pinned", 5);
        Seed(alice, "Alice unpinned", 1);
        var bobsThread = Seed(bob, "Bob pinned", 5);

        await Pin(alice).Handle(new PinConversationCommand(alicesThread), default);
        await Pin(bob).Handle(new PinConversationCommand(bobsThread), default);

        (await PageAsync(alice)).Pinned.Select(p => p.Title).Should().Equal("Alice pinned");
        (await PageAsync(bob)).Pinned.Select(p => p.Title).Should().Equal("Bob pinned");
    }

    // ══ ★★ The seam with the paging ═══════════════════════════════════════════

    [Fact]
    public async Task Pinned_come_back_newest_pin_first_regardless_of_conversation_age()
    {
        var alice = As(nameof(Pinned_come_back_newest_pin_first_regardless_of_conversation_age),
            Guid.NewGuid(), AliceId);
        var oldest = Seed(alice, "Oldest thread", 5_000);
        var newest = Seed(alice, "Newest thread", 1);

        // Pinned in the opposite order to their activity, so the two orderings cannot be confused.
        await Pin(alice, Now.AddMinutes(-10)).Handle(new PinConversationCommand(newest), default);
        await Pin(alice, Now).Handle(new PinConversationCommand(oldest), default);

        (await PageAsync(alice)).Pinned.Select(p => p.Title)
            .Should().Equal(new[] { "Oldest thread", "Newest thread" }, "most recently PINNED first");
    }

    [Fact]
    public async Task A_PINNED_CONVERSATION_NEVER_APPEARS_IN_THE_PAGED_FLOW()
    {
        // ★★ THE DUPLICATE THIS PREVENTS. Without the server-side exclusion a pinned thread comes back
        // twice — in its own group and in its time band. Fixing that in the browser would hide the
        // duplicate and leave batches of uneven size, so "25 rows" would sometimes mean 24 and the end
        // of the list would arrive early.
        var alice = As(nameof(A_PINNED_CONVERSATION_NEVER_APPEARS_IN_THE_PAGED_FLOW),
            Guid.NewGuid(), AliceId);

        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++) ids.Add(Seed(alice, $"C{i:D2}", i));

        await Pin(alice).Handle(new PinConversationCommand(ids[0]), default);   // newest
        await Pin(alice).Handle(new PinConversationCommand(ids[11]), default);  // oldest

        var seen = new List<string>();
        string? cursor = null;
        do
        {
            var batch = await PageAsync(alice, cursor, pageSize: 4);
            seen.AddRange(batch.Items.Select(i => i.Title));
            cursor = batch.NextCursor;
        } while (cursor is not null);

        seen.Should().NotContain("C00").And.NotContain("C11");
        seen.Should().HaveCount(10);
        seen.Should().OnlyHaveUniqueItems();

        var first = await PageAsync(alice, pageSize: 4);
        first.Pinned.Select(p => p.Title).Should().BeEquivalentTo(new[] { "C00", "C11" });
    }

    [Fact]
    public async Task AN_OLD_PINNED_CONVERSATION_IS_IN_THE_FIRST_RESPONSE()
    {
        // ★★ THE WHOLE POINT OF THE FEATURE. A thread from months ago sits far below any first batch;
        // if pins went through the cursor it simply would not be there, which is the problem pinning
        // exists to solve.
        var alice = As(nameof(AN_OLD_PINNED_CONVERSATION_IS_IN_THE_FIRST_RESPONSE), Guid.NewGuid(), AliceId);

        for (var i = 0; i < 60; i++) Seed(alice, $"Filler {i:D2}", i);
        var ancient = Seed(alice, "The important one", 100_000);

        await Pin(alice).Handle(new PinConversationCommand(ancient), default);

        var first = await PageAsync(alice, pageSize: 25);

        first.Items.Select(i => i.Title).Should().NotContain("The important one",
            "it is far below the first batch — that is the premise");
        first.Pinned.Select(p => p.Title).Should().Equal("The important one");
    }

    [Fact]
    public async Task PINNING_DOES_NOT_TOUCH_THE_CONVERSATIONS_UpdatedAt()
    {
        // ★★ IF IT DID, PINNING WOULD REORDER THE WHOLE LIST. The cursor keys on (UpdatedAt, Id), so a
        // save that bumped the conversation's timestamp would move it to the top AND invalidate every
        // cursor the user is holding mid-scroll. The pin lives on another table so this should be
        // impossible — and "should be impossible" is exactly the kind of thing that quietly stops being
        // true when somebody adds an auditing interceptor.
        var alice = As(nameof(PINNING_DOES_NOT_TOUCH_THE_CONVERSATIONS_UpdatedAt), Guid.NewGuid(), AliceId);

        var ids = new List<Guid>();
        for (var i = 0; i < 8; i++) ids.Add(Seed(alice, $"C{i:D2}", i));

        var before = await alice.Db.AssistantConversations
            .OrderBy(c => c.Id).Select(c => new { c.Id, c.UpdatedAt }).ToListAsync();

        var orderBefore = (await PageAsync(alice, pageSize: 100)).Items.Select(i => i.Title).ToList();

        await Pin(alice, Now.AddHours(5)).Handle(new PinConversationCommand(ids[4]), default);
        await Unpin(alice).Handle(new UnpinConversationCommand(ids[4]), default);

        var after = await alice.Db.AssistantConversations
            .OrderBy(c => c.Id).Select(c => new { c.Id, c.UpdatedAt }).ToListAsync();

        after.Should().BeEquivalentTo(before, "not one conversation's timestamp may move");

        // And the observable consequence: the same list, in the same order.
        (await PageAsync(alice, pageSize: 100)).Items.Select(i => i.Title).Should().Equal(orderBefore);
    }

    [Fact]
    public async Task SEARCHING_EMITS_NO_PINNED_GROUP_AND_EXCLUDES_NOTHING()
    {
        // ★ SEARCH IS A DIFFERENT MODE. Showing a pinned thread that does not match what was typed is
        // noise; hiding one that does match would be inconsistent. So the results come back flat — which
        // also means a pinned conversation that MATCHES must still appear among them.
        var alice = As(nameof(SEARCHING_EMITS_NO_PINNED_GROUP_AND_EXCLUDES_NOTHING),
            Guid.NewGuid(), AliceId);

        var pinned = Seed(alice, "Comisiones del Q3", 500);
        Seed(alice, "Comisiones del Q4", 1);
        Seed(alice, "Otra cosa", 2);

        await Pin(alice).Handle(new PinConversationCommand(pinned), default);

        var results = await PageAsync(alice, search: "Comisiones");

        results.Pinned.Should().BeEmpty("searching has no pinned group");
        results.Items.Select(i => i.Title).Should()
            .BeEquivalentTo(new[] { "Comisiones del Q3", "Comisiones del Q4" },
                "and a pinned thread that matches is still a result");
    }

    [Fact]
    public async Task The_pinned_group_rides_with_the_FIRST_batch_only()
    {
        var alice = As(nameof(The_pinned_group_rides_with_the_FIRST_batch_only), Guid.NewGuid(), AliceId);

        var ids = new List<Guid>();
        for (var i = 0; i < 10; i++) ids.Add(Seed(alice, $"C{i:D2}", i));
        await Pin(alice).Handle(new PinConversationCommand(ids[9]), default);

        var first = await PageAsync(alice, pageSize: 3);
        first.Pinned.Should().ContainSingle();

        var second = await PageAsync(alice, first.NextCursor, pageSize: 3);
        second.Pinned.Should().BeEmpty("a continuation already has the group on screen");
    }

    // ══ The cap ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Passing_the_cap_is_refused_with_a_TRANSLATION_KEY()
    {
        var alice = As(nameof(Passing_the_cap_is_refused_with_a_TRANSLATION_KEY), Guid.NewGuid(), AliceId);

        for (var i = 0; i < AssistantPins.MaxPinned; i++)
        {
            var id = Seed(alice, $"C{i:D3}", i);
            (await Pin(alice).Handle(new PinConversationCommand(id), default)).IsSuccess.Should().BeTrue();
        }

        var oneTooMany = Seed(alice, "One too many", 999);
        var result = await Pin(alice).Handle(new PinConversationCommand(oneTooMany), default);

        result.IsSuccess.Should().BeFalse();
        // ★ A KEY, NOT A SENTENCE — this list is read in three languages.
        result.Error.Should().Be(AssistantPins.LimitReachedKey);
        result.Error.Should().StartWith("ASSISTANT.");
    }

    [Fact]
    public async Task AT_THE_CAP_RE_PINNING_SOMETHING_ALREADY_PINNED_STILL_WORKS()
    {
        // ★ THE OFF-BY-ONE THE EARLY RETURN AVOIDS. At exactly the limit, pressing Pin on a row that is
        // already pinned adds nothing — so refusing it would be telling the user they cannot do the
        // thing that is already done.
        var alice = As(nameof(AT_THE_CAP_RE_PINNING_SOMETHING_ALREADY_PINNED_STILL_WORKS),
            Guid.NewGuid(), AliceId);

        var ids = new List<Guid>();
        for (var i = 0; i < AssistantPins.MaxPinned; i++)
        {
            ids.Add(Seed(alice, $"C{i:D3}", i));
            await Pin(alice).Handle(new PinConversationCommand(ids[i]), default);
        }

        (await Pin(alice).Handle(new PinConversationCommand(ids[0]), default))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Unpinning_frees_a_slot()
    {
        var alice = As(nameof(Unpinning_frees_a_slot), Guid.NewGuid(), AliceId);

        var ids = new List<Guid>();
        for (var i = 0; i < AssistantPins.MaxPinned; i++)
        {
            ids.Add(Seed(alice, $"C{i:D3}", i));
            await Pin(alice).Handle(new PinConversationCommand(ids[i]), default);
        }

        await Unpin(alice).Handle(new UnpinConversationCommand(ids[0]), default);

        var another = Seed(alice, "Now there is room", 999);
        (await Pin(alice).Handle(new PinConversationCommand(another), default))
            .IsSuccess.Should().BeTrue();
    }

    // ══ Deleting ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Deleting_a_conversation_removes_its_standing_row()
    {
        var alice = As(nameof(Deleting_a_conversation_removes_its_standing_row), Guid.NewGuid(), AliceId);
        var id = Seed(alice, "Q3", 5);

        await Pin(alice).Handle(new PinConversationCommand(id), default);
        (await alice.Db.AssistantConversationStates.CountAsync()).Should().Be(1);

        var delete = new DeleteConversationHandler(alice.Db, alice.User, Entitled());
        (await delete.Handle(new DeleteConversationCommand(id), default)).IsSuccess.Should().BeTrue();

        // ★ Asserted explicitly rather than trusted to the cascade: the InMemory provider does not
        // enforce cascades, so only the handler's own RemoveRange makes this true here — which is
        // precisely why the handler does both.
        (await alice.Db.AssistantConversationStates.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }
}
