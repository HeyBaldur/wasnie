using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Users;

/// <summary>
/// KAN-92, batch 4: the payee LIST is filtered by who is asking.
///
/// ★★ THE HOLE THIS CLOSES. Payees.Read answers "may this role read payee data", never "which
/// payees", and the list endpoint takes no payee id for a per-resource guard to check. So every role
/// holding the permission could page through the whole company: names, employee codes and email
/// addresses of every colleague. Managers still hold it, so the filter is still the thing that keeps
/// them to their own team.
///
/// ★★ KAN-93 TOOK THE PERMISSION OFF THE REP ENTIRELY, and these tests changed shape because of it.
/// "A rep must be able to see their own record" turned out to be false — their own record is what
/// /api/me/dashboard is for — and the payee screen was a dead end that offered them a creation form
/// the server refused. So the rep's expectation here is 403, not a filtered page. The filter itself
/// has NOT been weakened: it is what the Manager cases below still prove.
///
/// ★ THE TOTAL COUNT IS ASSERTED, NOT ONLY THE ROWS. A filter applied after paging would still report
/// how many people exist, and that number alone discloses the roster.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PayeeListVisibilityTests(TestDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private async Task<Guid> SeedPayeeAsync(string tag, string? ownerUserId = null, Guid? managerId = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var code = $"{tag}{Guid.NewGuid():N}"[..12];
        var payee = Payee.Create(TestConstants.TenantA, $"Payee {code}", code,
            $"{code}@test.com".ToLowerInvariant(), new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        if (ownerUserId is not null) payee.LinkToUser(ownerUserId, "test", Now);
        if (managerId is not null) payee.AssignManager(managerId.Value, "test", Now);
        db.Payees.Add(payee);
        await db.SaveChangesAsync();
        return payee.Id;
    }

    private sealed record Page(IReadOnlyList<Row> Items, int TotalCount);
    private sealed record Row(Guid Id, string FullName, string? Email);

    private static Task<HttpResponseMessage> GetAsync(TestDatabaseFixture fixture, string userId, string role) =>
        fixture.Factory.CreateClient()
            .WithAuth(TestConstants.TenantA, userId, role)
            .GetAsync("/api/payees?page=1&pageSize=100");

    private async Task<Page> ListAsync(string userId, string role)
    {
        var response = await GetAsync(fixture, userId, role);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonSerializer.Deserialize<Page>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// <summary>
    /// KAN-93, Bug 1. A rep does not get a filtered page any more — they get the door.
    ///
    /// ★★ IT ASSERTS THE ENDPOINT, NOT THE MENU, AND THAT IS THE POINT. The rail entry and the Angular
    /// route guard both read the same permission, but neither of them is a security boundary: a rep
    /// who types /api/payees has bypassed both. This is the line that makes hiding the menu honest.
    ///
    /// ★ EVEN LINKED, EVEN WITH A RECORD OF THEIR OWN. Seeding their payee first is deliberate — the
    /// refusal must not depend on there being nothing to show, or it would quietly become a filter
    /// again the day somebody links them.
    /// </summary>
    [Fact]
    public async Task A_rep_is_refused_the_payee_list_outright()
    {
        var repUser = $"user-{Guid.NewGuid():N}";
        await SeedPayeeAsync("VR", repUser);
        await SeedPayeeAsync("VO");

        var response = await GetAsync(fixture, repUser, "Rep");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_unlinked_rep_is_refused_for_the_same_reason()
    {
        await SeedPayeeAsync("VU");

        var response = await GetAsync(fixture, $"user-{Guid.NewGuid():N}", "Rep");

        // ★ THE SAME ANSWER AS A LINKED REP. Whether an account is attached to a payee must not change
        // the status code here, or the endpoint would report link state to anybody who asked.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// ★★ THE FILTER IS STILL THERE, AND THIS IS WHAT PROVES IT. Removing the rep's permission would
    /// be an easy way to accidentally delete the narrowing KAN-92 built — nothing would fail. A manager
    /// with an unlinked account resolves to PayeeVisibility.None, so an empty page is the correct
    /// answer and a full one is the old hole reopening.
    /// </summary>
    [Fact]
    public async Task An_unlinked_manager_gets_an_empty_page_rather_than_the_company()
    {
        await SeedPayeeAsync("VU");

        var page = await ListAsync($"user-{Guid.NewGuid():N}", "Manager");

        // ★ Empty is the correct answer, not a refusal: from where they stand there is nothing to list.
        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task A_manager_sees_themselves_and_their_direct_reports_only()
    {
        var managerUser = $"user-{Guid.NewGuid():N}";
        var managerPayee = await SeedPayeeAsync("VM", managerUser);
        var report = await SeedPayeeAsync("VD", managerId: managerPayee);
        var stranger = await SeedPayeeAsync("VS");

        var page = await ListAsync(managerUser, "Manager");

        page.Items.Select(i => i.Id).Should().Contain([managerPayee, report]);
        page.Items.Select(i => i.Id).Should().NotContain(stranger);
    }

    [Fact]
    public async Task An_administrator_still_sees_the_whole_tenant()
    {
        var seeded = await SeedPayeeAsync("VA");

        var page = await ListAsync($"user-{Guid.NewGuid():N}", "TenantAdmin");

        // Supervision of the whole tenant is their job; narrowing them would break the product.
        page.Items.Select(i => i.Id).Should().Contain(seeded);
        page.TotalCount.Should().BeGreaterThan(1);
    }
}
