using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Users.Handlers;
using Wasnie.Application.Features.Users.Queries;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-93: the unlinked-payee picker is a typeahead, not a roster.
///
/// ★★ THE CAP IS THE POINT AND THE CAP IS THE RISK. Returning every unlinked payee made both pickers
/// unusable past a few hundred names and shipped the whole staff list — names, codes, addresses — on
/// every modal open. Capping fixes that and quietly breaks two other things unless they are held down:
/// the email SUGGESTION, which used to be matched in memory over the complete list, and the "nobody is
/// free" notice, which used to be `payees.Count == 0`. Both are what most of this file is about.
/// </summary>
public sealed class UnlinkedPayeePickerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(ApplicationDbContext Db, ListUnlinkedPayeesHandler Handler, Guid TenantId);

    private static Harness Seed(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var auth = Substitute.For<IAuthorizationService>();
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        return new Harness(db, new ListUnlinkedPayeesHandler(db, auth), tenantId);
    }

    private static void AddPayee(
        Harness h, string fullName, string code, string? email = null, string? ownerUserId = null)
    {
        var payee = Payee.Create(h.TenantId, fullName, code, email,
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        if (ownerUserId is not null) payee.LinkToUser(ownerUserId, "test", Now);
        h.Db.Payees.Add(payee);
        h.Db.SaveChanges();
    }

    private static Task<Wasnie.Domain.Common.Results.Result<Wasnie.Application.Features.Users.DTOs.UnlinkedPayeesResponse>>
        Ask(Harness h, string? email = null, string? search = null) =>
        h.Handler.Handle(new ListUnlinkedPayeesQuery(email, search), default);

    /// <summary>
    /// ★★ THE WHOLE COMPLAINT, AS A TEST. A workspace of 60 unlinked payees used to come back whole;
    /// now it comes back as one page. The exact cap is the handler's business — what must never come
    /// back is "all of them".
    /// </summary>
    [Fact]
    public async Task A_large_workspace_comes_back_as_one_page_not_the_whole_roster()
    {
        var h = Seed(nameof(A_large_workspace_comes_back_as_one_page_not_the_whole_roster));
        for (var i = 0; i < 60; i++)
            AddPayee(h, $"Payee {i:D3}", $"EMP-{i:D3}");

        var result = await Ask(h);

        result.Value.Payees.Should().HaveCountLessThan(60);
        result.Value.TotalAvailable.Should().Be(60, "the count is of the workspace, not of the page");
    }

    [Fact]
    public async Task The_search_matches_the_name()
    {
        var h = Seed(nameof(The_search_matches_the_name));
        AddPayee(h, "Ana Garcia", "EMP-001");
        AddPayee(h, "Bruno Silva", "EMP-002");

        var result = await Ask(h, search: "garcia");

        result.Value.Payees.Select(p => p.FullName).Should().BeEquivalentTo(["Ana Garcia"]);
    }

    /// <summary>
    /// ★ NAME, CODE AND ADDRESS, because an administrator attaching somebody knows one of the three
    /// and not reliably which. A search that only matched names would refuse the employee code they
    /// just copied out of the HR system.
    /// </summary>
    [Theory]
    [InlineData("EMP-002")]
    [InlineData("bruno@acme.com")]
    public async Task The_search_matches_the_code_and_the_address_too(string query)
    {
        var h = Seed(nameof(The_search_matches_the_code_and_the_address_too) + query);
        AddPayee(h, "Ana Garcia", "EMP-001", "ana@acme.com");
        AddPayee(h, "Bruno Silva", "EMP-002", "bruno@acme.com");

        var result = await Ask(h, search: query);

        result.Value.Payees.Select(p => p.FullName).Should().BeEquivalentTo(["Bruno Silva"]);
    }

    [Fact]
    public async Task A_payee_that_already_belongs_to_a_login_is_never_offered()
    {
        var h = Seed(nameof(A_payee_that_already_belongs_to_a_login_is_never_offered));
        AddPayee(h, "Ana Garcia", "EMP-001");
        AddPayee(h, "Taken Person", "EMP-002", ownerUserId: "user-someone");

        var result = await Ask(h);

        result.Value.Payees.Select(p => p.FullName).Should().BeEquivalentTo(["Ana Garcia"]);
        result.Value.TotalAvailable.Should().Be(1);
    }

    /// <summary>
    /// ★★ THE REGRESSION THE CAP WOULD HAVE CAUSED, AND THE REASON THE SUGGESTION IS ITS OWN QUERY.
    /// It used to be matched in memory over a list holding EVERY unlinked payee, so it always found
    /// its man. Capped at twenty and sorted by name, a match sitting at position 400 is simply not in
    /// the page — and it fails SILENTLY: no error, just an administrator who never learns that a payee
    /// with the invitee's exact address exists, and links the wrong person by hand.
    /// </summary>
    [Fact]
    public async Task The_suggestion_is_found_even_when_it_falls_outside_the_page()
    {
        var h = Seed(nameof(The_suggestion_is_found_even_when_it_falls_outside_the_page));
        for (var i = 0; i < 60; i++)
            AddPayee(h, $"Payee {i:D3}", $"EMP-{i:D3}");

        // Sorts last by name, so it is far outside the first page.
        AddPayee(h, "Zoe Last", "EMP-ZZZ", "zoe@acme.com");

        var result = await Ask(h, email: "zoe@acme.com");

        result.Value.Payees.Should().NotContain(p => p.Email == "zoe@acme.com",
            "the page is sorted by name, so the match is genuinely outside it — otherwise this test proves nothing");
        result.Value.Suggested.Should().NotBeNull();
        result.Value.Suggested!.FullName.Should().Be("Zoe Last");
    }

    /// <summary>
    /// ★★ AND IT SURVIVES THE SEARCH BOX. The two inputs are independent: what the admin is typing into
    /// the dropdown has nothing to do with which record matches the invitee's address. Answered from
    /// the search results, the suggestion would vanish the moment they started typing a name.
    /// </summary>
    [Fact]
    public async Task The_suggestion_is_unaffected_by_what_is_typed_in_the_picker()
    {
        var h = Seed(nameof(The_suggestion_is_unaffected_by_what_is_typed_in_the_picker));
        AddPayee(h, "Ana Garcia", "EMP-001", "ana@acme.com");
        AddPayee(h, "Bruno Silva", "EMP-002", "bruno@acme.com");

        var result = await Ask(h, email: "ana@acme.com", search: "bruno");

        result.Value.Payees.Select(p => p.FullName).Should().BeEquivalentTo(["Bruno Silva"]);
        result.Value.Suggested!.FullName.Should().Be("Ana Garcia");
    }

    /// <summary>
    /// ★ A TAKEN PAYEE IS NOT SUGGESTED EITHER. Suggesting one would point the administrator at a
    /// record the link handler is going to refuse — an offer the product cannot honour.
    /// </summary>
    [Fact]
    public async Task A_matching_address_on_a_taken_payee_is_not_suggested()
    {
        var h = Seed(nameof(A_matching_address_on_a_taken_payee_is_not_suggested));
        AddPayee(h, "Taken Person", "EMP-002", "taken@acme.com", ownerUserId: "user-someone");

        var result = await Ask(h, email: "taken@acme.com");

        result.Value.Suggested.Should().BeNull();
    }

    /// <summary>
    /// ★★ ZERO AND ZERO ARE DIFFERENT ZEROS (§B3). "Every payee already belongs to a login" is fixed by
    /// unlinking somebody; "your search matched nothing" is fixed by typing again. `TotalAvailable` is
    /// the only thing on the response that can tell the screen which of the two it is looking at.
    /// </summary>
    [Fact]
    public async Task An_empty_search_result_still_reports_that_payees_exist()
    {
        var h = Seed(nameof(An_empty_search_result_still_reports_that_payees_exist));
        AddPayee(h, "Ana Garcia", "EMP-001");

        var result = await Ask(h, search: "nobody-by-this-name");

        result.Value.Payees.Should().BeEmpty();
        result.Value.TotalAvailable.Should().Be(1);
    }

    [Fact]
    public async Task A_workspace_where_everybody_is_linked_reports_nothing_available()
    {
        var h = Seed(nameof(A_workspace_where_everybody_is_linked_reports_nothing_available));
        AddPayee(h, "Taken Person", "EMP-002", ownerUserId: "user-someone");

        var result = await Ask(h);

        result.Value.Payees.Should().BeEmpty();
        result.Value.TotalAvailable.Should().Be(0);
    }
}
