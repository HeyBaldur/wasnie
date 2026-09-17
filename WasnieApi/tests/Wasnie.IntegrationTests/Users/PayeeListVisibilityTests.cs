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
/// ★★ THE HOLE THIS CLOSES. Payees.Read is held by every role, legitimately — a rep must be able to
/// see their own record. It answers "may this role read payee data", never "which payees", and the
/// list endpoint takes no payee id for a per-resource guard to check. So a rep could page through the
/// whole company: names, employee codes and email addresses of every colleague.
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

    private async Task<Page> ListAsync(string userId, string role)
    {
        var response = await fixture.Factory.CreateClient()
            .WithAuth(TestConstants.TenantA, userId, role)
            .GetAsync("/api/payees?page=1&pageSize=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonSerializer.Deserialize<Page>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    [Fact]
    public async Task A_rep_sees_only_their_own_record_and_a_total_that_says_so()
    {
        var repUser = $"user-{Guid.NewGuid():N}";
        var mine = await SeedPayeeAsync("VR", repUser);
        await SeedPayeeAsync("VO");
        await SeedPayeeAsync("VO");

        var page = await ListAsync(repUser, "Rep");

        page.Items.Select(i => i.Id).Should().BeEquivalentTo([mine]);
        page.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task An_unlinked_account_gets_an_empty_page_rather_than_the_company()
    {
        await SeedPayeeAsync("VU");

        var page = await ListAsync($"user-{Guid.NewGuid():N}", "Rep");

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
