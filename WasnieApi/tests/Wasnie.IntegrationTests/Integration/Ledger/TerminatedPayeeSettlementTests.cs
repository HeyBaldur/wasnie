using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Ledger;

/// <summary>
/// The end of a payee's life in the ledger: their account is frozen, listed, and closed by a person.
///
/// The engine stops processing someone who has left — which is right, and which is also how a debt
/// becomes invisible if nothing else changes. So the freeze ships with a work queue: finance can see
/// exactly whose account is still open, and closes it with one of two TYPED entries. Wasnie records
/// that decision; it never makes it and never collects the money.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class TerminatedPayeeSettlementTests(TestDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";
    private const string Endpoint = "/api/payees/ledger/terminated-with-balance";

    private async Task<Guid> SeedPayeeAsync(string code, bool terminated, decimal balance)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenantId = TestConstants.TenantA;

        var payee = Payee.Create(tenantId, $"Payee {code}", code, $"{code}-{Guid.NewGuid():N}@test.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        if (terminated)
            payee.MarkAsTerminated(new DateOnly(2026, 6, 30), "hr@acme.com", Now);
        db.Payees.Add(payee);

        if (balance != 0m)
        {
            var entry = PayeeLedgerEntry.CreateSystemEntry(
                tenantId, payee.Id, LedgerTransactionType.ClawbackDebit, Money.Of(Math.Abs(balance), Eur),
                "Churned deal.", LedgerSourceType.DealChurn, "system",
                Guid.NewGuid(), Now, Guid.NewGuid());
            var row = PayeeBalance.Open(tenantId, payee.Id, Eur, Guid.NewGuid(), Now);
            row.Apply(entry, Now);
            db.PayeeLedgerEntries.Add(entry);
            db.PayeeBalances.Add(row);
        }
        else
        {
            db.PayeeBalances.Add(PayeeBalance.Open(tenantId, payee.Id, Eur, Guid.NewGuid(), Now));
        }

        await db.SaveChangesAsync();
        return payee.Id;
    }

    private async Task<List<JsonElement>> ListAsync(HttpClient client)
    {
        var response = await client.GetAsync(Endpoint);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    // ══ The work queue ═══════════════════════════════════════════════════════

    [Fact]
    public async Task The_list_shows_a_departed_payee_whose_account_is_still_open()
    {
        var payeeId = await SeedPayeeAsync("LEFT-OWING", terminated: true, balance: -500m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        var row = (await ListAsync(client)).Single(r => r.GetProperty("payeeId").GetGuid() == payeeId);

        row.GetProperty("balance").GetDecimal().Should().Be(-500m, "signed exactly as stored");
        row.GetProperty("currency").GetString().Should().Be(Eur);
        row.GetProperty("terminationDate").GetString().Should().Be("2026-06-30");
    }

    [Fact]
    public async Task A_departed_payee_who_owes_nothing_is_not_in_the_queue()
    {
        // The queue is work to do. A settled account is not work.
        var payeeId = await SeedPayeeAsync("LEFT-SETTLED", terminated: true, balance: 0m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        (await ListAsync(client)).Should().NotContain(r => r.GetProperty("payeeId").GetGuid() == payeeId);
    }

    [Fact]
    public async Task An_active_payee_with_debt_is_not_in_the_queue_either()
    {
        // Their debt is not orphaned — the engine still nets it from what they earn.
        var payeeId = await SeedPayeeAsync("STILL-HERE", terminated: false, balance: -800m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        (await ListAsync(client)).Should().NotContain(r => r.GetProperty("payeeId").GetGuid() == payeeId);
    }

    [Fact]
    public async Task Reading_the_queue_requires_a_token()
    {
        var response = await fixture.Factory.CreateClient().GetAsync(Endpoint);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ══ Closing the account ══════════════════════════════════════════════════

    [Theory]
    [InlineData("ExternalSettlementCredit")]
    [InlineData("WriteOffCredit")]
    public async Task Finance_closes_the_account_and_it_leaves_the_queue(string type)
    {
        var payeeId = await SeedPayeeAsync($"CLOSE-{type}", terminated: true, balance: -500m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        var post = await client.PostAsJsonAsync($"/api/payees/{payeeId}/ledger/adjustments", new
        {
            transactionType = type,
            amount = 500m,
            currency = Eur,
            justification = "Account closed on termination.",
        });

        post.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var balance = await db.PayeeBalances.IgnoreQueryFilters().SingleAsync(b => b.PayeeId == payeeId);
        balance.Balance.Amount.Should().Be(0m, "the closing credit settles the account");

        // Append-only: the debit that created the debt is still there, next to the entry that closed it.
        var entries = await db.PayeeLedgerEntries.IgnoreQueryFilters()
            .Where(e => e.PayeeId == payeeId).ToListAsync();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.TransactionType == LedgerTransactionType.ClawbackDebit
                                   && e.Amount.Amount == -500m);
        var closing = entries.Single(e => e.TransactionType.ToString() == type);
        closing.Origin.Should().Be(LedgerEntryOrigin.Human, "a person decided this, not the engine");
        closing.CreatedBy.Should().NotBe("system");

        (await ListAsync(client)).Should().NotContain(r => r.GetProperty("payeeId").GetGuid() == payeeId);
    }

    [Fact]
    public async Task The_two_closing_types_stay_distinguishable_for_reporting()
    {
        // "Recovered through payroll" and "we ate the loss" must be countable separately without
        // reading anyone's justification text. That is the whole reason they are two types.
        var recovered = await SeedPayeeAsync("REPORT-REC", terminated: true, balance: -300m);
        var lost = await SeedPayeeAsync("REPORT-LOST", terminated: true, balance: -700m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        await client.PostAsJsonAsync($"/api/payees/{recovered}/ledger/adjustments", new
        {
            transactionType = "ExternalSettlementCredit", amount = 300m, currency = Eur,
            justification = "Deducted from the final paycheck.",
        });
        await client.PostAsJsonAsync($"/api/payees/{lost}/ledger/adjustments", new
        {
            transactionType = "WriteOffCredit", amount = 700m, currency = Eur,
            justification = "Uncollectable — written off.",
        });

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var recoveredTotal = await db.PayeeLedgerEntries.IgnoreQueryFilters()
            .Where(e => e.TransactionType == LedgerTransactionType.ExternalSettlementCredit
                     && e.PayeeId == recovered)
            .SumAsync(e => e.Amount.Amount);
        var writtenOffTotal = await db.PayeeLedgerEntries.IgnoreQueryFilters()
            .Where(e => e.TransactionType == LedgerTransactionType.WriteOffCredit && e.PayeeId == lost)
            .SumAsync(e => e.Amount.Amount);

        recoveredTotal.Should().Be(300m);
        writtenOffTotal.Should().Be(700m);
    }

    [Fact]
    public async Task A_rep_cannot_close_an_account()
    {
        // Closing an account is a finance decision. Ledger.Adjust, not Ledger.Read.
        var payeeId = await SeedPayeeAsync("RBAC-CLOSE", terminated: true, balance: -500m);
        var rep = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Rep");

        var response = await rep.PostAsJsonAsync($"/api/payees/{payeeId}/ledger/adjustments", new
        {
            transactionType = "WriteOffCredit", amount = 500m, currency = Eur,
            justification = "Trying to write off my own debt.",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
