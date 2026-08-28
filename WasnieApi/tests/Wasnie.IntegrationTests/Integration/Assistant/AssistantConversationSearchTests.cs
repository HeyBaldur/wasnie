using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Assistant;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Assistant;

/// <summary>
/// The conversation list against a REAL SQL Server: paging end to end, and the half of the search that
/// only exists in the database.
///
/// ★★ WHY THIS FILE HAS TO EXIST ALONGSIDE THE UNIT TESTS. Case- and accent-insensitivity is a property
/// of the Title column's COLLATION, and the InMemory provider the unit tests run on has no collations —
/// it evaluates `Contains` in .NET, which is ordinal and case-sensitive. A unit test asserting
/// "asignacion finds Asignación" would fail against a perfectly correct implementation. The behaviour is
/// real only where the collation is, so it is proven here.
///
/// ★ AND IT IS THE FEATURE, NOT A NICETY. These titles are generated from the user's own first question,
/// in Spanish, English and Polish. Nobody reaches for the accent key while searching their own history,
/// so an accent-sensitive match is a search box that fails on exactly the words it was built for.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class AssistantConversationSearchTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _client = null!;

    public AssistantConversationSearchTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await ClearConversationsAsync();
        _client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task ClearConversationsAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM AssistantConversationStates");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM AssistantMessages");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM AssistantConversations");
    }

    /// <summary>
    /// Seeds with raw SQL, exactly as the fixture's own reset helpers do.
    ///
    /// ★ NOT THROUGH THE DbContext'S ENTITY API, and that is not a style choice. A scope resolved
    /// outside a request has no HTTP context, so ITenantContext there is the BACKGROUND-JOB one, which
    /// throws on read until a job calls SetTenant. Adding rows through the tracked entity would trip
    /// that before it ever reached the database — and the whole point of these rows is that they belong
    /// to tenants and users the CALLER is not, which no request-scoped context could write anyway.
    /// </summary>
    private async Task<Guid> SeedAsync(
        string title, int minutesOld, Guid? tenantId = null, string? userId = null)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var when = DateTimeOffset.UtcNow.AddMinutes(-minutesOld);
        var id = Guid.NewGuid();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO AssistantConversations (Id, TenantId, UserId, Title, CreatedAt, UpdatedAt)
            VALUES ({id}, {tenantId ?? TestConstants.TenantA},
                    {userId ?? TestConstants.UserAId}, {title}, {when}, {when})
            """);

        return id;
    }

    private sealed record Page(List<Summary> Items, string? NextCursor, List<Summary> Pinned);

    private sealed record Summary(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int MessageCount);

    private async Task<Page> GetAsync(string query = "")
    {
        var response = await _client.GetAsync($"/api/assistant/conversations{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Page>())!;
    }

    // ══ ★ The half that only the database can prove ═══════════════════════════

    [Theory]
    [InlineData("asignacion")]   // no accent typed, accented in the title
    [InlineData("Asignación")]   // exactly as written
    [InlineData("ASIGNACION")]   // shouted, unaccented
    [InlineData("asignación")]   // accented, lower case
    public async Task SEARCH_IGNORES_CASE_AND_ACCENTS(string term)
    {
        await SeedAsync("Asignación de planes del Q3", 1);
        await SeedAsync("Something else entirely", 2);

        var page = await GetAsync($"?search={Uri.EscapeDataString(term)}");

        page.Items.Should().ContainSingle(
            $"'{term}' has to find the same row — nobody reaches for the accent key while searching")
            .Which.Title.Should().Be("Asignación de planes del Q3");
    }

    [Fact]
    public async Task Search_matches_a_word_from_the_middle_of_the_title()
    {
        // Titles are written from the user's first question, so the word somebody remembers is almost
        // never the first one. A prefix match would find nothing they actually look for.
        await SeedAsync("Cómo calculo la comisión de Ana", 1);

        (await GetAsync("?search=comision")).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task A_percent_sign_is_searched_for_literally()
    {
        // ★ EF translates `Contains` to CHARINDEX, not LIKE, so a wildcard typed into the box is just a
        // character. Under a hand-written LIKE this test would return the whole list.
        await SeedAsync("Descuento del 50% en EMEA", 1);
        await SeedAsync("Nothing to do with it", 2);

        (await GetAsync("?search=50%25")).Items.Should().ContainSingle();
    }

    // ══ Paging, end to end ════════════════════════════════════════════════════

    [Fact]
    public async Task Walking_every_batch_over_HTTP_returns_each_conversation_once()
    {
        for (var i = 0; i < 12; i++) await SeedAsync($"Conversation {i:D2}", i);

        var seen = new List<string>();
        var query = "?pageSize=5";

        while (true)
        {
            var page = await GetAsync(query);
            seen.AddRange(page.Items.Select(i => i.Title));

            if (page.NextCursor is null) break;
            query = $"?pageSize=5&cursor={Uri.EscapeDataString(page.NextCursor)}";
        }

        seen.Should().HaveCount(12);
        seen.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task THE_CURSOR_SURVIVES_A_QUERY_STRING_ROUND_TRIP()
    {
        // ★ THE REASON IT IS URL-SAFE BASE64. A '+' in a query string means a space; plain Base64 would
        // work in most tests and corrupt in production the first time a cursor happened to contain one.
        // Only a real HTTP round trip can catch that, which is why this assertion lives here.
        for (var i = 0; i < 4; i++) await SeedAsync($"Conversation {i:D2}", i);

        var first = await GetAsync("?pageSize=2");
        first.NextCursor.Should().NotBeNull();

        // Deliberately NOT escaped: a cursor that needs escaping to survive is not URL-safe.
        var second = await GetAsync($"?pageSize=2&cursor={first.NextCursor}");

        second.Items.Should().HaveCount(2);
        second.Items.Select(i => i.Title).Should().NotIntersectWith(first.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task A_batch_size_over_the_maximum_is_refused()
    {
        var response = await _client.GetAsync("/api/assistant/conversations?pageSize=5000");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unreadable_cursor_starts_over_instead_of_failing()
    {
        await SeedAsync("Only one", 1);

        var page = await GetAsync("?cursor=not-a-real-cursor");

        page.Items.Should().ContainSingle("a stale cursor is not a broken screen");
    }

    // ══ ★ Scoping ═════════════════════════════════════════════════════════════

    [Fact]
    public async Task NEITHER_PAGING_NOR_SEARCH_CROSSES_A_USER_OR_A_TENANT()
    {
        // ★★ THE LOAD-BEARING ONE, over the wire and through the real query filter. Both parameters are
        // ways of ASKING for rows, so both have to be proven unable to reach rows that are not the
        // caller's — including the case that catches a missing user filter: the SAME user id in a
        // different tenant.
        await SeedAsync("Mine and searchable", 1);
        await SeedAsync("Theirs and searchable", 2, userId: TestConstants.UserBId);
        await SeedAsync("Another tenant searchable", 3, tenantId: TestConstants.TenantB);
        await SeedAsync("Another tenant same user searchable", 4,
            tenantId: TestConstants.TenantB, userId: TestConstants.UserAId);

        var listed = await GetAsync("?pageSize=100");
        listed.Items.Select(i => i.Title).Should().Equal("Mine and searchable");

        var searched = await GetAsync("?search=searchable&pageSize=100");
        searched.Items.Select(i => i.Title).Should().Equal("Mine and searchable");
    }

    [Fact]
    public async Task Without_a_token_the_list_is_refused()
    {
        var anonymous = _fixture.Factory.CreateClient();

        (await anonymous.GetAsync("/api/assistant/conversations")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
    // == ★ Pinning, over the wire and through the real schema ==========================

    [Fact]
    public async Task A_PINNED_CONVERSATION_IS_RETURNED_WHOLE_AND_EXCLUDED_FROM_THE_BATCHES()
    {
        // ★★ THE SEAM, END TO END. Pinned threads are the OLD ones, so they ride outside the cursor -
        // and must therefore not also appear inside it, or the same row renders twice.
        for (var i = 0; i < 8; i++) await SeedAsync($"Filler {i:D2}", i);
        var ancient = await SeedAsync("The important one", 100_000);

        (await _client.PostAsync($"/api/assistant/conversations/{ancient}/pin", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var first = await GetAsync("?pageSize=4");

        first.Pinned.Select(p => p.Title).Should().Equal("The important one");
        first.Items.Select(i => i.Title).Should().NotContain("The important one");

        var seen = new List<string>(first.Items.Select(i => i.Title));
        var cursor = first.NextCursor;
        while (cursor is not null)
        {
            var batch = await GetAsync($"?pageSize=4&cursor={Uri.EscapeDataString(cursor)}");
            seen.AddRange(batch.Items.Select(i => i.Title));
            batch.Pinned.Should().BeEmpty("the group rides with the first batch only");
            cursor = batch.NextCursor;
        }

        seen.Should().NotContain("The important one");
        seen.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task PINNING_IS_IDEMPOTENT_AND_THE_UNIQUE_KEY_HOLDS()
    {
        // ★ The unique index is what makes "one row per (user, conversation)" true under a race rather
        // than usually true - and only a real database has it.
        var id = await SeedAsync("Q3", 5);

        for (var i = 0; i < 3; i++)
        {
            (await _client.PostAsync($"/api/assistant/conversations/{id}/pin", null))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await GetAsync()).Pinned.Should().ContainSingle();

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.AssistantConversationStates.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Unpinning_puts_it_back_into_the_paged_flow()
    {
        var id = await SeedAsync("Q3", 5);

        await _client.PostAsync($"/api/assistant/conversations/{id}/pin", null);
        (await GetAsync()).Items.Select(i => i.Title).Should().NotContain("Q3");

        (await _client.DeleteAsync($"/api/assistant/conversations/{id}/pin"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await GetAsync();
        after.Pinned.Should().BeEmpty();
        after.Items.Select(i => i.Title).Should().Contain("Q3");
    }

    [Fact]
    public async Task PINNING_ANOTHER_USERS_OR_TENANTS_CONVERSATION_IS_REFUSED_INDISTINGUISHABLY()
    {
        // ★★ THE LOAD-BEARING ONE, through the real query filter. "Not yours", "another tenant's"
        // and "never existed" all answer 404 - a 403 would confirm that something is there.
        var theirs = await SeedAsync("Theirs", 5, userId: TestConstants.UserBId);
        var otherTenant = await SeedAsync("Another tenant", 5, tenantId: TestConstants.TenantB);

        foreach (var id in new[] { theirs, otherTenant, Guid.NewGuid() })
        {
            (await _client.PostAsync($"/api/assistant/conversations/{id}/pin", null))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.AssistantConversationStates.IgnoreQueryFilters().CountAsync())
            .Should().Be(0, "nothing was written for anybody");
    }

    [Fact]
    public async Task Deleting_a_pinned_conversation_leaves_no_standing_row_behind()
    {
        var id = await SeedAsync("Q3", 5);
        await _client.PostAsync($"/api/assistant/conversations/{id}/pin", null);

        (await _client.DeleteAsync($"/api/assistant/conversations/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.AssistantConversationStates.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Searching_returns_no_pinned_group()
    {
        var id = await SeedAsync("Comisiones del Q3", 500);
        await SeedAsync("Comisiones del Q4", 1);
        await _client.PostAsync($"/api/assistant/conversations/{id}/pin", null);

        var results = await GetAsync("?search=Comisiones");

        results.Pinned.Should().BeEmpty();
        results.Items.Select(i => i.Title).Should()
            .BeEquivalentTo(new[] { "Comisiones del Q3", "Comisiones del Q4" });
    }

    [Fact]
    public async Task Without_a_token_pinning_is_refused()
    {
        var anonymous = _fixture.Factory.CreateClient();

        (await anonymous.PostAsync($"/api/assistant/conversations/{Guid.NewGuid()}/pin", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
