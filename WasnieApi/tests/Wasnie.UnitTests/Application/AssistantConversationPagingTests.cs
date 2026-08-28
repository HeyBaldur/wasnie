using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.DTOs;
using Wasnie.Application.Assistant.Handlers;
using Wasnie.Application.Assistant.Queries;
using Wasnie.Application.Assistant.Validators;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Assistant;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Paging and searching the conversation list.
///
/// ★ WHY THE LIST STOPPED BEING RETURNED WHOLE. It was every row, on the reasoning that one person's
/// own chat history is not a tenant-wide list. At a couple of thousand conversations the payload is
/// still only a few hundred kilobytes — but the DOM is two thousand rows, and the drawer took a visible
/// beat to open every single time. The cost was never the bytes.
///
/// ★ AND THE TESTS THAT MATTER HERE ARE NOT THE SPEED ONES. Speed is not what a cursor buys over an
/// offset; CORRECTNESS is. This list is ordered by last activity, so answering a question in any thread
/// moves it to the top and shifts everything below it down one place. Under OFFSET, a user paging
/// through then skips a conversation they have never seen and gets a duplicate of one they have. Those
/// two — the insertion test and the tie test — are the reason this file exists.
/// </summary>
public sealed class AssistantConversationPagingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 9, 0, 0, TimeSpan.Zero);

    private const string AliceId = "user-alice";
    private const string BobId = "user-bob";

    private sealed record Principal(ApplicationDbContext Db, ICurrentUserService User, Guid TenantId);

    /// <summary>
    /// One principal over a NAMED shared store, so two principals in the same test read the same rows
    /// through different identities — which is the only way an isolation test can prove anything.
    /// </summary>
    private static Principal As(string dbName, Guid tenantId, string userId)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(AssistantConversationPagingTests)}.{dbName}")
                .Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(userId);

        return new Principal(db, user, tenantId);
    }

    private static IAssistantEntitlement Entitled()
    {
        var e = Substitute.For<IAssistantEntitlement>();
        e.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        e.RequireAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return e;
    }

    private static ListConversationsHandler List(Principal p) => new(p.Db, p.User, Entitled());

    private static Task<AssistantConversationPageDto> PageAsync(
        Principal p, string? cursor = null, int? pageSize = null, string? search = null) =>
        List(p).Handle(new ListConversationsQuery(cursor, pageSize, search), CancellationToken.None)
            .ContinueWith(t => t.Result.Value!);

    /// <summary>
    /// Seeds one conversation. <paramref name="minutesOld"/> drives UpdatedAt, so the expected order is
    /// readable at the call site instead of being reconstructed from timestamps.
    /// </summary>
    private static AssistantConversation Seed(
        Principal p, string title, int minutesOld, Guid? id = null, string? userId = null)
    {
        var when = Now.AddMinutes(-minutesOld);
        var conversation = AssistantConversation.Start(
            id ?? Guid.NewGuid(), p.TenantId, userId ?? p.User.UserId!, title, when);

        p.Db.AssistantConversations.Add(conversation);
        p.Db.SaveChanges();
        return conversation;
    }

    /// <summary>Walks every batch and returns the titles in the order the user would actually see them.</summary>
    private static async Task<List<string>> WalkAsync(Principal p, int pageSize, string? search = null)
    {
        var seen = new List<string>();
        string? cursor = null;

        do
        {
            var page = await PageAsync(p, cursor, pageSize, search);
            seen.AddRange(page.Items.Select(i => i.Title));
            cursor = page.NextCursor;
        }
        // A runaway guard: a broken cursor that never advances would otherwise hang the suite instead
        // of failing it.
        while (cursor is not null && seen.Count < 500);

        return seen;
    }

    // ══ Batching ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task The_first_batch_holds_exactly_the_size_asked_for_and_says_where_to_continue()
    {
        var alice = As(nameof(The_first_batch_holds_exactly_the_size_asked_for_and_says_where_to_continue),
            Guid.NewGuid(), AliceId);
        for (var i = 0; i < 10; i++) Seed(alice, $"C{i:D2}", i);

        var page = await PageAsync(alice, pageSize: 4);

        page.Items.Should().HaveCount(4);
        page.Items.Select(i => i.Title).Should().Equal(
            new[] { "C00", "C01", "C02", "C03" }, "newest activity first");
        page.NextCursor.Should().NotBeNull();
    }

    [Fact]
    public async Task Walking_every_batch_returns_each_conversation_exactly_once()
    {
        var alice = As(nameof(Walking_every_batch_returns_each_conversation_exactly_once),
            Guid.NewGuid(), AliceId);
        for (var i = 0; i < 23; i++) Seed(alice, $"C{i:D2}", i);

        var seen = await WalkAsync(alice, pageSize: 5);

        seen.Should().HaveCount(23);
        seen.Should().OnlyHaveUniqueItems("a row must never appear in two batches");
        seen.Should().BeInAscendingOrder("titles were seeded newest-first, so this is the list order");
    }

    [Fact]
    public async Task The_last_batch_reports_no_cursor()
    {
        var alice = As(nameof(The_last_batch_reports_no_cursor), Guid.NewGuid(), AliceId);
        for (var i = 0; i < 6; i++) Seed(alice, $"C{i:D2}", i);

        var first = await PageAsync(alice, pageSize: 3);
        var second = await PageAsync(alice, first.NextCursor, pageSize: 3);

        second.Items.Should().HaveCount(3);
        second.NextCursor.Should().BeNull("there is nothing after this, so the button disappears");
    }

    [Fact]
    public async Task A_total_that_lands_exactly_on_the_batch_size_does_not_promise_an_empty_batch()
    {
        // ★ THE OFF-BY-ONE THIS DESIGN AVOIDS. Inferring "there is more" from a FULL batch would offer
        // Load more here, and the next batch would come back empty — the user clicks and nothing
        // happens. Fetching one row beyond the batch answers the question instead of guessing it.
        var alice = As(nameof(A_total_that_lands_exactly_on_the_batch_size_does_not_promise_an_empty_batch),
            Guid.NewGuid(), AliceId);
        for (var i = 0; i < 5; i++) Seed(alice, $"C{i:D2}", i);

        var page = await PageAsync(alice, pageSize: 5);

        page.Items.Should().HaveCount(5);
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task An_empty_history_is_an_empty_batch_with_no_cursor()
    {
        var alice = As(nameof(An_empty_history_is_an_empty_batch_with_no_cursor), Guid.NewGuid(), AliceId);

        var page = await PageAsync(alice);

        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
    }

    // ══ ★ The two failures an OFFSET produces ═════════════════════════════════

    [Fact]
    public async Task A_CONVERSATION_ARRIVING_MID_WALK_NEITHER_DUPLICATES_NOR_SKIPS_ANYTHING()
    {
        // ★★ THE TEST THIS WHOLE MECHANISM EXISTS FOR. Under OFFSET, a row inserted at the TOP while
        // somebody is paging shifts every later row down one place: batch two then starts one past
        // where batch one ended, so one conversation is silently never shown and another is shown
        // twice. Nothing in the UI can detect that. A cursor names a ROW, so what happens above it is
        // none of its business.
        var alice = As(nameof(A_CONVERSATION_ARRIVING_MID_WALK_NEITHER_DUPLICATES_NOR_SKIPS_ANYTHING),
            Guid.NewGuid(), AliceId);
        for (var i = 0; i < 12; i++) Seed(alice, $"C{i:D2}", i + 1);

        var first = await PageAsync(alice, pageSize: 4);
        first.Items.Select(i => i.Title).Should().Equal("C00", "C01", "C02", "C03");

        // Someone answers a question in a new thread: it lands at the very top, above everything the
        // walk has already passed.
        Seed(alice, "BRAND NEW", minutesOld: 0);

        var rest = new List<string>();
        var cursor = first.NextCursor;
        while (cursor is not null)
        {
            var page = await PageAsync(alice, cursor, pageSize: 4);
            rest.AddRange(page.Items.Select(i => i.Title));
            cursor = page.NextCursor;
        }

        var seen = first.Items.Select(i => i.Title).Concat(rest).ToList();

        seen.Should().OnlyHaveUniqueItems("nothing may be shown twice");
        seen.Should().Contain(new[] { "C04", "C05", "C06", "C07", "C08", "C09", "C10", "C11" },
            "and nothing below the insertion point may be skipped");
        seen.Should().NotContain("BRAND NEW",
            "it sorts ABOVE the cursor, so it belongs to a batch this walk already passed — it appears "
            + "on the next fresh load, which is honest, rather than shoving the list around mid-walk");
    }

    [Fact]
    public async Task CONVERSATIONS_SHARING_A_TIMESTAMP_KEEP_A_STABLE_ORDER_ACROSS_BATCHES()
    {
        // ★★ THE TIEBREAKER. UpdatedAt is not unique — a seeded fixture, a bulk import, or two turns in
        // the same millisecond all produce ties. Ordering by the timestamp alone leaves tied rows in
        // whatever order the database feels like, and the cursor's boundary then either skips every row
        // sharing that instant or returns them forever.
        var alice = As(nameof(CONVERSATIONS_SHARING_A_TIMESTAMP_KEEP_A_STABLE_ORDER_ACROSS_BATCHES),
            Guid.NewGuid(), AliceId);

        // Ten rows, all at the SAME instant, with ids spread across the range so the id ordering is not
        // accidentally the insertion order.
        for (var i = 0; i < 10; i++)
        {
            Seed(alice, $"TIE{i:D2}", minutesOld: 5, id: new Guid($"0000000{i}-0000-0000-0000-00000000000{i}"));
        }

        var seen = await WalkAsync(alice, pageSize: 3);

        seen.Should().HaveCount(10);
        seen.Should().OnlyHaveUniqueItems("a tie must not repeat across batches");

        // And the order is the same on a second walk — that is what "stable" means.
        var again = await WalkAsync(alice, pageSize: 4);
        again.Should().Equal(seen, "a different batch size must not change WHICH rows come back or when");
    }

    // ══ Page size ═════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AssistantPaging.MaxPageSize + 1)]
    [InlineData(5_000)]
    public void A_batch_size_outside_the_range_is_rejected(int pageSize)
    {
        var result = new ListConversationsQueryValidator()
            .Validate(new ListConversationsQuery(PageSize: pageSize));

        result.IsValid.Should().BeFalse("silently substituting a number lets the caller's mistake live");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(AssistantPaging.MaxPageSize)]
    public void A_batch_size_inside_the_range_or_absent_is_accepted(int? pageSize)
    {
        new ListConversationsQueryValidator()
            .Validate(new ListConversationsQuery(PageSize: pageSize))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task No_batch_size_takes_the_default()
    {
        var alice = As(nameof(No_batch_size_takes_the_default), Guid.NewGuid(), AliceId);
        for (var i = 0; i < AssistantPaging.DefaultPageSize + 5; i++) Seed(alice, $"C{i:D3}", i);

        var page = await PageAsync(alice);

        page.Items.Should().HaveCount(AssistantPaging.DefaultPageSize);
    }

    // ══ Search ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SEARCH_FINDS_A_CONVERSATION_THAT_IS_NOT_IN_THE_FIRST_BATCH()
    {
        // ★★ THE LIE THIS ENDS. Filtering in the browser searched only what had been loaded, so once the
        // list was paged a real conversation forty rows down came back as "no results" — the same class
        // of untruth as telling somebody a payee has no plans because the lookup could not see them.
        var alice = As(nameof(SEARCH_FINDS_A_CONVERSATION_THAT_IS_NOT_IN_THE_FIRST_BATCH),
            Guid.NewGuid(), AliceId);
        for (var i = 0; i < 60; i++) Seed(alice, $"Filler {i:D2}", i);
        Seed(alice, "Comisiones del cuarto trimestre", minutesOld: 500);

        var unfiltered = await PageAsync(alice, pageSize: 25);
        unfiltered.Items.Select(i => i.Title).Should().NotContain("Comisiones del cuarto trimestre",
            "the row is far below the first batch — that is the premise of this test");

        var found = await PageAsync(alice, search: "trimestre");

        found.Items.Select(i => i.Title).Should().ContainSingle()
            .Which.Should().Be("Comisiones del cuarto trimestre");
    }

    [Fact]
    public async Task Search_matches_a_word_from_the_middle_of_a_title()
    {
        var alice = As(nameof(Search_matches_a_word_from_the_middle_of_a_title), Guid.NewGuid(), AliceId);
        Seed(alice, "Plan Comercial EMEA", 1);
        Seed(alice, "Otra cosa", 2);

        (await PageAsync(alice, search: "Comercial")).Items.Should().ContainSingle();
    }

    // ★★ CASE- AND ACCENT-INSENSITIVITY IS NOT TESTED IN THIS FILE, AND THAT IS NOT AN OVERSIGHT.
    //
    // It is a property of the Title COLUMN'S COLLATION (AssistantConversationConfiguration), and the
    // InMemory provider these tests run on has no collations at all — it evaluates `Contains` in .NET,
    // which is ordinal and case-SENSITIVE. A test asserting insensitivity here would either fail
    // against a correct implementation or, if written to pass, would be asserting .NET's string
    // comparison rather than the database's.
    //
    // So it is proven where it is real: AssistantConversationSearchTests, against SQL Server.

    [Fact]
    public async Task Search_results_are_paged_by_the_same_mechanism()
    {
        var alice = As(nameof(Search_results_are_paged_by_the_same_mechanism), Guid.NewGuid(), AliceId);
        for (var i = 0; i < 9; i++) Seed(alice, $"Comisiones {i:D2}", i);
        for (var i = 0; i < 5; i++) Seed(alice, $"Otra cosa {i:D2}", 50 + i);

        var seen = await WalkAsync(alice, pageSize: 4, search: "Comisiones");

        seen.Should().HaveCount(9);
        seen.Should().OnlyHaveUniqueItems();
        seen.Should().OnlyContain(t => t.StartsWith("Comisiones"),
            "the filter must survive every batch, not just the first");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("c")]
    [InlineData("  c  ")]
    public async Task A_term_below_the_minimum_is_ignored_rather_than_refused(string term)
    {
        // The user is mid-word, not asking a question. Erroring here would flash a failure between the
        // first and second keystroke of every search anybody types.
        var alice = As(nameof(A_term_below_the_minimum_is_ignored_rather_than_refused) + term.Trim(),
            Guid.NewGuid(), AliceId);
        Seed(alice, "Alpha", 1);
        Seed(alice, "Beta", 2);

        var page = await PageAsync(alice, search: term);

        page.Items.Should().HaveCount(2, "the ordinary list comes back untouched");
    }

    [Fact]
    public async Task A_search_matching_nothing_is_an_empty_batch_and_not_an_error()
    {
        var alice = As(nameof(A_search_matching_nothing_is_an_empty_batch_and_not_an_error),
            Guid.NewGuid(), AliceId);
        Seed(alice, "Alpha", 1);

        var page = await PageAsync(alice, search: "zzzz");

        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
    }

    // ══ ★ Scoping ═════════════════════════════════════════════════════════════

    [Fact]
    public async Task PAGING_AND_SEARCH_NEVER_CROSS_A_USER_OR_A_TENANT()
    {
        // ★★ THE LOAD-BEARING TEST. Everything else in this file would still be true of a list that
        // leaked; this is the one that makes the feature private. Both new parameters are ways of
        // ASKING for rows, so both have to be proven unable to reach rows that are not the caller's.
        var db = nameof(PAGING_AND_SEARCH_NEVER_CROSS_A_USER_OR_A_TENANT);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var alice = As(db, tenantA, AliceId);
        var bob = As(db, tenantA, BobId);          // same tenant, different person
        var carol = As(db, tenantB, AliceId);      // SAME user id, different tenant

        for (var i = 0; i < 8; i++) Seed(alice, $"Alice secreto {i}", i);
        for (var i = 0; i < 8; i++) Seed(bob, $"Bob secreto {i}", i);
        for (var i = 0; i < 8; i++) Seed(carol, $"Carol secreto {i}", i);

        // Walking every batch, not just the first: a leak that only shows on page three is still a leak.
        (await WalkAsync(alice, pageSize: 3)).Should().OnlyContain(t => t.StartsWith("Alice"));
        (await WalkAsync(bob, pageSize: 3)).Should().OnlyContain(t => t.StartsWith("Bob"));
        (await WalkAsync(carol, pageSize: 3)).Should().OnlyContain(t => t.StartsWith("Carol"));

        // And the search cannot reach across either boundary.
        (await PageAsync(alice, search: "secreto")).Items.Should().OnlyContain(i => i.Title.StartsWith("Alice"));
        (await PageAsync(bob, search: "secreto")).Items.Should().OnlyContain(i => i.Title.StartsWith("Bob"));
        (await PageAsync(carol, search: "secreto")).Items.Should().OnlyContain(i => i.Title.StartsWith("Carol"));
    }

    [Fact]
    public async Task A_CURSOR_FROM_ANOTHER_USERS_LIST_BUYS_NO_ACCESS()
    {
        // ★ A CURSOR IS A POSITION, NOT A PERMISSION. It reaches the client, so it can be replayed by
        // somebody else — and it must do nothing except say where to continue inside a set that was
        // already narrowed to the caller.
        var db = nameof(A_CURSOR_FROM_ANOTHER_USERS_LIST_BUYS_NO_ACCESS);
        var tenantA = Guid.NewGuid();
        var alice = As(db, tenantA, AliceId);
        var bob = As(db, tenantA, BobId);

        for (var i = 0; i < 6; i++) Seed(bob, $"Bob {i}", i);
        Seed(alice, "Alice only", 3);

        var bobsCursor = (await PageAsync(bob, pageSize: 2)).NextCursor;
        bobsCursor.Should().NotBeNull();

        var page = await PageAsync(alice, bobsCursor, pageSize: 10);

        page.Items.Should().OnlyContain(i => i.Title.StartsWith("Alice"));
    }

    // ══ The cursor itself ═════════════════════════════════════════════════════

    [Fact]
    public void A_cursor_round_trips_to_the_tick()
    {
        // Sub-second precision is not a detail: a cursor that loses it lands on a DIFFERENT row than the
        // one it was built from, which is the duplicate-or-skip bug arriving through the encoder.
        var original = new ConversationCursor(
            new DateTimeOffset(2026, 8, 26, 9, 15, 30, 123, TimeSpan.FromHours(2)).AddTicks(4567),
            Guid.NewGuid());

        ConversationCursor.Decode(original.Encode()).Should().Be(original);
    }

    [Fact]
    public void A_cursor_is_safe_to_put_in_a_query_string()
    {
        // '+' means a space in a query string and '/' and '=' need escaping — a plain Base64 cursor
        // survives most round trips and corrupts on the ones where something helpfully decodes it.
        var encoded = new ConversationCursor(Now, Guid.NewGuid()).Encode();

        encoded.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!")]
    [InlineData("Zm9vYmFy")]                 // valid base64, not a cursor
    [InlineData("MjAyNi0wOC0yNnxub3QtYS1ndWlk")] // right shape, unparseable id
    public async Task An_unreadable_cursor_starts_over_instead_of_failing(string? cursor)
    {
        // ★ A STALE CURSOR IS NOT AN ERROR. They travel through URLs and bookmarks and go stale; a 400
        // turns a harmless staleness into a broken screen. Starting over is always a correct answer to
        // "continue from a place that no longer parses" — and it leaks nothing, because the scoping does
        // not depend on the cursor.
        ConversationCursor.Decode(cursor).Should().BeNull();

        var alice = As(nameof(An_unreadable_cursor_starts_over_instead_of_failing) + (cursor ?? "null"),
            Guid.NewGuid(), AliceId);
        Seed(alice, "Alpha", 1);

        (await PageAsync(alice, cursor)).Items.Should().ContainSingle();
    }
}
