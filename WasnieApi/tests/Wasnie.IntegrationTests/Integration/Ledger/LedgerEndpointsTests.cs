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
/// The PIPE, not the domain: authorization attributes on the controller, the actor the pipeline
/// injects into a ledger entry, and JSON serialisation of the statement DTO.
///
/// A handler test cannot catch any of these — it starts after auth ran and after the body was
/// bound. For an endpoint that moves a salesperson's balance, that gap is the whole risk.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class LedgerEndpointsTests(TestDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    private async Task<Guid> SeedPayeeAsync(string code)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var payee = Payee.Create(TestConstants.TenantA, $"Payee {code}", code, $"{code}@test.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        db.Payees.Add(payee);
        await db.SaveChangesAsync();
        return payee.Id;
    }

    private async Task SeedBalanceAsync(Guid payeeId, decimal debt)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var entry = PayeeLedgerEntry.CreateSystemEntry(
            TestConstants.TenantA, payeeId, LedgerTransactionType.ClawbackDebit,
            Money.Of(debt, "EUR"), "Deal churned.", LedgerSourceType.DealChurn,
            "system", Guid.NewGuid(), Now, Guid.NewGuid());
        var balance = PayeeBalance.Open(TestConstants.TenantA, payeeId, "EUR", Guid.NewGuid(), Now);
        balance.Apply(entry, Now);
        db.PayeeLedgerEntries.Add(entry);
        db.PayeeBalances.Add(balance);
        await db.SaveChangesAsync();
    }

    private static object Adjustment(string type = "ClawbackForgivenessCredit", decimal amount = 100m) =>
        new { transactionType = type, amount, currency = "EUR", justification = "Agreed with the rep." };

    // ══ RBAC on the controller ═══════════════════════════════════════════════

    [Fact]
    public async Task A_rep_cannot_post_an_adjustment_even_by_calling_the_endpoint_directly()
    {
        // THE test a handler test cannot do: it proves the authorization runs in the pipeline,
        // not merely that domain logic exists behind it. A Rep holds Ledger.Read but not Ledger.Adjust.
        var payeeId = await SeedPayeeAsync("RBAC-REP");
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Rep");

        var response = await client.PostAsJsonAsync(
            $"/api/payees/{payeeId}/ledger/adjustments", Adjustment());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // And nothing was written.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.PayeeLedgerEntries.IgnoreQueryFilters()
            .CountAsync(e => e.PayeeId == payeeId)).Should().Be(0);
    }

    [Fact]
    public async Task A_manager_cannot_post_an_adjustment_either()
    {
        var payeeId = await SeedPayeeAsync("RBAC-MGR");
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Manager");

        var response = await client.PostAsJsonAsync(
            $"/api/payees/{payeeId}/ledger/adjustments", Adjustment());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Finance_can_post_an_adjustment_and_it_is_persisted()
    {
        var payeeId = await SeedPayeeAsync("RBAC-FIN");
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        var response = await client.PostAsJsonAsync(
            $"/api/payees/{payeeId}/ledger/adjustments", Adjustment());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entry = await db.PayeeLedgerEntries.IgnoreQueryFilters()
            .SingleAsync(e => e.PayeeId == payeeId);
        entry.Amount.Amount.Should().Be(100m);
        entry.Origin.Should().Be(LedgerEntryOrigin.Human);
    }

    // ══ The actor comes from the authenticated user ══════════════════════════

    [Fact]
    public async Task The_persisted_entry_carries_the_authenticated_user_not_a_default()
    {
        var payeeId = await SeedPayeeAsync("ACTOR-1");
        var client = fixture.Factory.CreateClient()
            .WithAuth(TestConstants.TenantA, userId: TestConstants.UserBId, role: "TenantAdmin");

        var response = await client.PostAsJsonAsync(
            $"/api/payees/{payeeId}/ledger/adjustments", Adjustment("ManualBonusCredit", 250m));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entry = await db.PayeeLedgerEntries.IgnoreQueryFilters()
            .SingleAsync(e => e.PayeeId == payeeId);

        // AuthTestHelper puts "{userId}@test.com" in the email claim; the handler prefers email.
        entry.CreatedBy.Should().Be($"{TestConstants.UserBId}@test.com");
        entry.CreatedBy.Should().NotBe("system", "a system actor would mean the pipeline lost the user");
    }

    [Fact]
    public async Task The_payee_comes_from_the_route_not_from_the_body()
    {
        // The URL the caller was authorised against is the balance that must move.
        var routePayee = await SeedPayeeAsync("ROUTE-1");
        var otherPayee = await SeedPayeeAsync("ROUTE-2");
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "TenantAdmin");

        var response = await client.PostAsJsonAsync(
            $"/api/payees/{routePayee}/ledger/adjustments",
            new
            {
                payeeId = otherPayee,          // ignored on purpose
                transactionType = "ManualBonusCredit",
                amount = 75m,
                currency = "EUR",
                justification = "Route wins.",
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.PayeeLedgerEntries.IgnoreQueryFilters().CountAsync(e => e.PayeeId == routePayee))
            .Should().Be(1);
        (await db.PayeeLedgerEntries.IgnoreQueryFilters().CountAsync(e => e.PayeeId == otherPayee))
            .Should().Be(0);
    }

    [Fact]
    public async Task An_adjustment_without_a_justification_is_rejected_over_HTTP_and_writes_nothing()
    {
        var payeeId = await SeedPayeeAsync("NOJUST");
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "TenantAdmin");

        var response = await client.PostAsJsonAsync(
            $"/api/payees/{payeeId}/ledger/adjustments",
            new { transactionType = "ManualBonusCredit", amount = 100m, currency = "EUR", justification = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.PayeeLedgerEntries.IgnoreQueryFilters().CountAsync(e => e.PayeeId == payeeId))
            .Should().Be(0);
    }

    [Fact]
    public async Task An_engine_only_type_is_rejected_over_HTTP()
    {
        var payeeId = await SeedPayeeAsync("ENGINEONLY");
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "TenantAdmin");

        var response = await client.PostAsJsonAsync(
            $"/api/payees/{payeeId}/ledger/adjustments", Adjustment("ClawbackDebit"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ══ Read access and DTO serialisation ════════════════════════════════════

    [Fact]
    public async Task A_rep_can_read_a_statement_transparency_is_the_point()
    {
        var payeeId = await SeedPayeeAsync("READ-REP");
        await SeedBalanceAsync(payeeId, 800m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Rep");

        var response = await client.GetAsync($"/api/payees/{payeeId}/ledger/statement");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Ledger.Read includes the Rep — a rep seeing why their pay shrank is the differentiator");
    }

    [Fact]
    public async Task The_statement_serialises_with_a_null_cap_the_multi_plan_case()
    {
        // No payouts behind the balance yet, so no single cap can be named: capPercentApplied is
        // null. This is the shape a client will actually receive, and it must be valid JSON, not a
        // contract exception.
        var payeeId = await SeedPayeeAsync("JSON-NULL");
        await SeedBalanceAsync(payeeId, 800m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "TenantAdmin");

        var response = await client.GetAsync($"/api/payees/{payeeId}/ledger/statement");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var statement = doc.RootElement.EnumerateArray().Single();

        statement.GetProperty("capPercentApplied").ValueKind.Should().Be(JsonValueKind.Null);
        statement.GetProperty("capLimited").GetBoolean().Should().BeFalse();
        statement.GetProperty("currency").GetString().Should().Be("EUR");
        statement.GetProperty("previousDebt").GetDecimal().Should().Be(-800m);
        statement.GetProperty("newCarryover").GetDecimal().Should().Be(-800m);
        statement.GetProperty("payRunId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_entries_endpoint_serialises_every_nullable_source_field()
    {
        var payeeId = await SeedPayeeAsync("JSON-ENTRIES");
        await SeedBalanceAsync(payeeId, 800m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "TenantAdmin");

        var response = await client.GetAsync($"/api/payees/{payeeId}/ledger/entries");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement.EnumerateArray().Single();

        entry.GetProperty("origin").GetString().Should().Be("System");
        entry.GetProperty("transactionType").GetString().Should().Be("ClawbackDebit");
        entry.GetProperty("amount").GetDecimal().Should().Be(-800m);
        entry.GetProperty("sourceTransactionId").ValueKind.Should().Be(JsonValueKind.Null);
        entry.GetProperty("daysActive").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_unauthenticated_request_is_refused_on_both_read_and_write()
    {
        var payeeId = await SeedPayeeAsync("ANON");
        var client = fixture.Factory.CreateClient();

        (await client.GetAsync($"/api/payees/{payeeId}/ledger/statement"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync($"/api/payees/{payeeId}/ledger/adjustments", Adjustment()))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ══ Plan clawback policy endpoint ════════════════════════════════════════

    [Fact]
    public async Task A_rep_cannot_change_a_plans_clawback_policy()
    {
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Rep");

        var response = await client.PutAsJsonAsync(
            $"/api/plans/{Guid.NewGuid()}/clawback-policy",
            new { maturationDays = 90, capPercent = 50m });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
