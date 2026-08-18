using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Ledger;

/// <summary>
/// The BOLA/IDOR fix, exercised THROUGH THE HTTP PIPELINE against real SQL Server.
///
/// ★ WHY NOT ONLY THE HANDLER TESTS. The vulnerability was never visible from a handler: the UI simply
/// did not offer a link to a colleague's ledger, so the hole existed exclusively at the level these
/// tests operate on — a hand-made request with somebody else's payee id in the URL. A guard proven only
/// by unit tests is a guard proven everywhere except where the attack happens.
///
/// ★ WHY REAL SQL. The filtered unique index on (TenantId, UserId) and the tenant query filter are both
/// database behaviour. EF InMemory ignores filtered indexes entirely, so an in-memory-only pass would
/// stay green against a schema that could not hold the data.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PayeeResourceAuthorizationTests(TestDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Creates a payee, optionally linked to a user and/or reporting to a manager.</summary>
    private async Task<Guid> SeedPayeeAsync(
        string code, string? userId = null, Guid? managerId = null, Guid? tenantId = null,
        bool terminated = false)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var payee = Payee.Create(
            tenantId ?? TestConstants.TenantA, $"Payee {code}", code, $"{code}@test.com".ToLowerInvariant(),
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);

        if (userId is not null) payee.LinkToUser(userId, "test", Now);
        if (managerId is not null) payee.AssignManager(managerId.Value, "test", Now);
        if (terminated) payee.MarkAsTerminated(new DateOnly(2026, 6, 30), "test", Now);

        db.Payees.Add(payee);
        await db.SaveChangesAsync();
        return payee.Id;
    }

    private async Task SeedDebtAsync(Guid payeeId, decimal debt, Guid? tenantId = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = tenantId ?? TestConstants.TenantA;
        var entry = PayeeLedgerEntry.CreateSystemEntry(
            tenant, payeeId, LedgerTransactionType.ClawbackDebit,
            Money.Of(debt, "EUR"), "Deal churned.", LedgerSourceType.DealChurn,
            "system", Guid.NewGuid(), Now, Guid.NewGuid());
        var balance = PayeeBalance.Open(tenant, payeeId, "EUR", Guid.NewGuid(), Now);
        balance.Apply(entry, Now);

        db.PayeeLedgerEntries.Add(entry);
        db.PayeeBalances.Add(balance);
        await db.SaveChangesAsync();
    }

    private HttpClient Client(string role, string userId, Guid? tenantId = null) =>
        fixture.Factory.CreateClient().WithAuth(tenantId ?? TestConstants.TenantA, userId, role);

    // ══ 1. A rep cannot read a colleague's money ═════════════════════════════

    [Fact]
    public async Task A_rep_cannot_read_another_payees_statement_or_entries()
    {
        var user = $"user-{Guid.NewGuid():N}";
        await SeedPayeeAsync("BOLA-OWN-1", userId: user);
        var victimId = await SeedPayeeAsync("BOLA-VICTIM-1", userId: $"user-{Guid.NewGuid():N}");
        await SeedDebtAsync(victimId, 800m);

        var rep = Client("Rep", user);

        (await rep.GetAsync($"/api/payees/{victimId}/ledger/statement"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await rep.GetAsync($"/api/payees/{victimId}/ledger/entries"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await rep.GetAsync($"/api/payees/{victimId}/dashboard"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// ★ THE ANTI-ENUMERATION PROPERTY. "Exists but is not yours" and "does not exist" must be the same
    /// answer down to the byte — otherwise the endpoint is an oracle that maps out which payee ids are
    /// real, and a rep can at minimum count their colleagues.
    /// </summary>
    [Fact]
    public async Task A_foreign_payee_and_a_nonexistent_one_produce_the_identical_answer()
    {
        var user = $"user-{Guid.NewGuid():N}";
        await SeedPayeeAsync("BOLA-OWN-2", userId: user);
        var victimId = await SeedPayeeAsync("BOLA-VICTIM-2", userId: $"user-{Guid.NewGuid():N}");
        await SeedDebtAsync(victimId, 500m);

        var rep = Client("Rep", user);

        var foreign = await rep.GetAsync($"/api/payees/{victimId}/ledger/statement");
        var imaginary = await rep.GetAsync($"/api/payees/{Guid.NewGuid()}/ledger/statement");

        foreign.StatusCode.Should().Be(imaginary.StatusCode);
        (await foreign.Content.ReadAsStringAsync())
            .Should().Be(await imaginary.Content.ReadAsStringAsync());

        var foreignEntries = await rep.GetAsync($"/api/payees/{victimId}/ledger/entries");
        var imaginaryEntries = await rep.GetAsync($"/api/payees/{Guid.NewGuid()}/ledger/entries");

        foreignEntries.StatusCode.Should().Be(imaginaryEntries.StatusCode);
        (await foreignEntries.Content.ReadAsStringAsync())
            .Should().Be(await imaginaryEntries.Content.ReadAsStringAsync());
    }

    // ══ 2. A rep CAN read their own ══════════════════════════════════════════

    [Fact]
    public async Task A_rep_can_read_their_own_statement_and_entries()
    {
        var user = $"user-{Guid.NewGuid():N}";
        var ownId = await SeedPayeeAsync("BOLA-OWN-3", userId: user);
        await SeedDebtAsync(ownId, 300m);

        var rep = Client("Rep", user);

        (await rep.GetAsync($"/api/payees/{ownId}/ledger/statement"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await rep.GetAsync($"/api/payees/{ownId}/ledger/entries"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ══ 3. Supervisory roles keep full access ════════════════════════════════

    [Theory]
    [InlineData("TenantAdmin")]
    [InlineData("CompManager")]
    public async Task Supervisory_roles_read_any_payee(string role)
    {
        var payeeId = await SeedPayeeAsync($"BOLA-SUP-{role}", userId: $"user-{Guid.NewGuid():N}");
        await SeedDebtAsync(payeeId, 200m);

        var client = Client(role, $"user-{Guid.NewGuid():N}");

        (await client.GetAsync($"/api/payees/{payeeId}/ledger/statement"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ══ 4. Fail-closed: an unlinked payee belongs to nobody ══════════════════

    /// <summary>
    /// The state of EVERY payee immediately after the B25 migration — no UserId anywhere. The safe
    /// behaviour is that reps see nothing until an admin links them, never that an unlinked record is
    /// open to whoever asks.
    /// </summary>
    [Fact]
    public async Task An_unlinked_payee_is_refused_to_a_rep_but_served_to_finance()
    {
        var user = $"user-{Guid.NewGuid():N}";
        await SeedPayeeAsync("BOLA-OWN-4", userId: user);
        var unlinkedId = await SeedPayeeAsync("BOLA-UNLINKED");
        await SeedDebtAsync(unlinkedId, 400m);

        (await Client("Rep", user).GetAsync($"/api/payees/{unlinkedId}/ledger/statement"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client("CompManager", $"user-{Guid.NewGuid():N}")
            .GetAsync($"/api/payees/{unlinkedId}/ledger/statement"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_rep_with_no_linked_payee_at_all_is_refused()
    {
        var orphanId = await SeedPayeeAsync("BOLA-ORPHAN", userId: $"user-{Guid.NewGuid():N}");
        await SeedDebtAsync(orphanId, 100m);

        var stranger = Client("Rep", $"user-{Guid.NewGuid():N}");

        (await stranger.GetAsync($"/api/payees/{orphanId}/ledger/statement"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ══ 5. Manager: own + direct reports, and nothing else ═══════════════════

    [Fact]
    public async Task A_manager_reads_their_direct_report_but_not_a_stranger()
    {
        var managerUser = $"user-{Guid.NewGuid():N}";
        var managerPayeeId = await SeedPayeeAsync("BOLA-MGR", userId: managerUser);
        var reportId = await SeedPayeeAsync(
            "BOLA-REPORT", userId: $"user-{Guid.NewGuid():N}", managerId: managerPayeeId);
        var strangerId = await SeedPayeeAsync("BOLA-STRANGER", userId: $"user-{Guid.NewGuid():N}");
        await SeedDebtAsync(reportId, 250m);
        await SeedDebtAsync(strangerId, 250m);

        var manager = Client("Manager", managerUser);

        (await manager.GetAsync($"/api/payees/{reportId}/ledger/statement"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.GetAsync($"/api/payees/{strangerId}/ledger/statement"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ══ 6. Cross-tenant stays closed ═════════════════════════════════════════

    [Fact]
    public async Task A_rep_of_another_tenant_cannot_read_the_payee()
    {
        var user = $"user-{Guid.NewGuid():N}";
        var payeeId = await SeedPayeeAsync("BOLA-TENANT-A", userId: user);
        await SeedDebtAsync(payeeId, 700m);

        // SAME user id, SAME role — only the tenant claim differs. Without the tenant filter this would
        // be an ownership match, which is exactly why this test carries the user id across.
        var otherTenantRep = Client("Rep", user, TestConstants.TenantB);

        (await otherTenantRep.GetAsync($"/api/payees/{payeeId}/ledger/statement"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherTenantRep.GetAsync($"/api/payees/{payeeId}/ledger/entries"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ══ 7. The list endpoint: filtered, not refused ══════════════════════════

    /// <summary>
    /// terminated-with-balance takes no payee id, so it could never be protected by a per-resource
    /// check: it returned every departed colleague's outstanding debt to anyone holding Ledger.Read.
    /// </summary>
    [Fact]
    public async Task The_terminated_accounts_list_is_filtered_to_what_the_caller_may_see()
    {
        var user = $"user-{Guid.NewGuid():N}";
        await SeedPayeeAsync("BOLA-LIST-OWN", userId: user);
        var departedId = await SeedPayeeAsync(
            "BOLA-LIST-GONE", userId: $"user-{Guid.NewGuid():N}", terminated: true);
        await SeedDebtAsync(departedId, 900m);

        var repBody = await (await Client("Rep", user)
            .GetAsync("/api/payees/ledger/terminated-with-balance")).Content.ReadAsStringAsync();
        var financeBody = await (await Client("CompManager", $"user-{Guid.NewGuid():N}")
            .GetAsync("/api/payees/ledger/terminated-with-balance")).Content.ReadAsStringAsync();

        repBody.Should().NotContain(departedId.ToString(),
            "a rep must not be handed a departed colleague's outstanding debt");
        financeBody.Should().Contain(departedId.ToString(),
            "finance's work queue must keep working — this is the whole point of the screen");
    }
}
