using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Assistant;

/// <summary>
/// The balance the assistant reads and the balance the payee's screen shows are the same numbers.
///
/// ★★ WHY THAT NEEDED A TEST OF ITS OWN. A user was told 1,280 EUR were pending, ran the pay run the
/// assistant recommended, and watched the figure not move. The amount was a clawback balance in their
/// favour — money owed TO them because more had been withheld than was owed — which a pay run cannot
/// settle. It had always been counted inside `awaitingPayment`, indistinguishably from an unpaid
/// commission, so the assistant described it faithfully and prescribed the wrong subsystem.
///
/// ★ AND IT IS AN INTEGRATION TEST BECAUSE THE SIGN LIVES IN THE DATABASE. PayeeBalance is a
/// materialised projection with a real rowversion; the positive/negative convention that decides
/// "owes us" from "we owe them" is only exercised against a real ledger. The unit tests pin the prompt
/// and the manual; this pins the arithmetic.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class AssistantClawbackBalanceTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _client = null!;

    public AssistantClawbackBalanceTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetCompensationDataAsync();
        await ClearBalancesAsync();
        _client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task ClearBalancesAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM PayeeLedgerEntries");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM PayeeBalances");
    }

    private async Task<Guid> SeedPayeeAsync(string code)
    {
        var response = await _client.PostAsJsonAsync("/api/payees", new
        {
            fullName = $"Clawback {code}",
            employeeCode = code,
            email = $"{code}@test.io".ToLowerInvariant(),
            hireDate = "2024-01-01",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedPayee>();
        return created!.Id;
    }

    private sealed record CreatedPayee(Guid Id);

    /// <summary>
    /// Writes a balance row directly.
    ///
    /// ★ RAW SQL, like the fixture's own helpers: a scope resolved outside a request has the
    /// background-job tenant context, which throws on read until a job sets a tenant.
    /// </summary>
    private async Task SeedBalanceAsync(Guid payeeId, decimal amount, string currency = "EUR")
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PayeeBalances (Id, TenantId, PayeeId, Currency, Balance, BalanceCurrency, UpdatedAt)
            VALUES ({Guid.NewGuid()}, {TestConstants.TenantA}, {payeeId}, {currency},
                    {amount}, {currency}, {DateTimeOffset.UtcNow})
            """);
    }

    private sealed record Summary(List<CurrencyRow> ByCurrency);

    private sealed record CurrencyRow(
        string Currency,
        decimal EarnedCommissionsInPeriod,
        decimal PaidOutInPeriod,
        decimal AwaitingPaymentAllTime,
        decimal OutstandingDebt,
        decimal NetPendingPayout,
        decimal ClawbackCreditAllTime);

    private async Task<CurrencyRow?> SummaryAsync(Guid payeeId)
    {
        var response = await _client.GetAsync($"/api/payees/{payeeId}/ledger/summary?period=all-time");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<Summary>();
        return body!.ByCurrency.SingleOrDefault(c => c.Currency == "EUR");
    }

    // ══ ★ The reported case ═══════════════════════════════════════════════════

    [Fact]
    public async Task A_BALANCE_IN_THE_PAYEES_FAVOUR_IS_NAMED_AND_NOT_JUST_SUMMED()
    {
        // ★★ THE 1,280 EUR. A positive ledger balance is money owed TO the payee. It counts towards what
        // is pending — that part was always right — but it is NOT an unpaid commission, and a pay run
        // cannot settle it. The breakdown is what lets the answer say so.
        var payeeId = await SeedPayeeAsync("CLW-001");
        await SeedBalanceAsync(payeeId, 1_280m);

        var row = await SummaryAsync(payeeId);

        row.Should().NotBeNull();
        row!.ClawbackCreditAllTime.Should().Be(1_280m, "it is named");
        row.AwaitingPaymentAllTime.Should().Be(1_280m, "and still counted, exactly as before");
        row.OutstandingDebt.Should().Be(0m, "a credit is not a debt");
        row.NetPendingPayout.Should().Be(1_280m);
    }

    [Fact]
    public async Task THE_TOTAL_DID_NOT_MOVE_it_was_only_broken_down()
    {
        // ★ THE PROPERTY THAT MAKES THIS CHANGE SAFE. Everything that already read awaitingPayment or
        // netPendingPayout keeps reading the same figure; what is new is knowing which part of it a pay
        // run can settle. If this ever fails, the change stopped being a breakdown and became a
        // correction — which is a different, much larger decision.
        var payeeId = await SeedPayeeAsync("CLW-002");
        await SeedBalanceAsync(payeeId, 500m);

        var row = await SummaryAsync(payeeId);

        row!.AwaitingPaymentAllTime.Should().Be(row.ClawbackCreditAllTime + 0m,
            "no payouts were seeded, so the credit is the whole of the pending side");
        row.NetPendingPayout.Should().Be(row.AwaitingPaymentAllTime - row.OutstandingDebt);
    }

    [Fact]
    public async Task A_NEGATIVE_BALANCE_IS_A_DEBT_AND_NOT_A_CREDIT()
    {
        // The ordinary clawback: the payee owes the company. The sign is the whole concept, and getting
        // it backwards would tell somebody they are owed money they actually owe.
        var payeeId = await SeedPayeeAsync("CLW-003");
        await SeedBalanceAsync(payeeId, -1_500m);

        var row = await SummaryAsync(payeeId);

        row!.OutstandingDebt.Should().Be(1_500m);
        row.ClawbackCreditAllTime.Should().Be(0m, "a debt must never be reported as money owed to them");
        row.AwaitingPaymentAllTime.Should().Be(0m);
    }

    [Fact]
    public async Task A_PAYEE_WITH_NO_LEDGER_ROW_REPORTS_NO_CREDIT()
    {
        // ★ THE FIELD MUST NOT APPEAR ON EVERY ORDINARY BALANCE. Almost nobody has a clawback credit,
        // and a number that is 0.00 on every answer is a number the assistant eventually mentions for
        // something to say.
        var payeeId = await SeedPayeeAsync("CLW-004");

        var row = await SummaryAsync(payeeId);

        (row?.ClawbackCreditAllTime ?? 0m).Should().Be(0m);
    }

    [Fact]
    public async Task The_summary_is_scoped_to_the_callers_tenant()
    {
        var payeeId = await SeedPayeeAsync("CLW-005");
        await SeedBalanceAsync(payeeId, 900m);

        var otherTenant = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);

        (await otherTenant.GetAsync($"/api/payees/{payeeId}/ledger/summary?period=all-time"))
            .StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}
