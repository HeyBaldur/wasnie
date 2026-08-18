using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Payouts;
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

    /// <param name="ownerUserId">
    /// The identity user this payee belongs to, or null for an unlinked one. Only the tests that assert
    /// a rep reads their OWN data need it — everything else here runs as a supervisory role, for which
    /// ownership is irrelevant.
    /// </param>
    private async Task<Guid> SeedPayeeAsync(string code, string? ownerUserId = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var payee = Payee.Create(TestConstants.TenantA, $"Payee {code}", code, $"{code}@test.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        if (ownerUserId is not null) payee.LinkToUser(ownerUserId, "test", Now);
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

    /// <summary>
    /// ★ THIS TEST'S SETUP CHANGED WITH THE BOLA FIX, ITS CLAIM DID NOT. It always meant "a rep may see
    /// THEIR OWN balance", but until Payee.UserId existed there was no way to say whose payee it was —
    /// so it seeded an unowned payee and passed for the wrong reason: the endpoint served ANY payee to
    /// any rep. The link is now established explicitly, which is what makes the assertion mean what the
    /// name says. See PayeeResourceAuthorizationTests for the other half (a foreign payee is refused).
    /// </summary>
    [Fact]
    public async Task A_rep_can_read_a_statement_transparency_is_the_point()
    {
        var repUser = $"user-{Guid.NewGuid():N}";
        var payeeId = await SeedPayeeAsync("READ-REP", repUser);
        await SeedBalanceAsync(payeeId, 800m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, repUser, "Rep");

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
        // The live balance is the field that answers "what do they owe" — always populated.
        statement.GetProperty("currentBalance").GetDecimal().Should().Be(-800m);
        // The run's figures are null because there IS no run. This assertion used to expect −800 in
        // previousDebt/newCarryover: those fields doubled as the live balance, which is precisely the
        // overloading that made a screen read a run's carryover as today's debt.
        statement.GetProperty("previousDebt").ValueKind.Should().Be(JsonValueKind.Null);
        statement.GetProperty("newCarryover").ValueKind.Should().Be(JsonValueKind.Null);
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

    // ══ The statement contract: live balance vs the photograph of a run ══════
    // The bug this pins: `newCarryover` used to mean the live balance when no run had settled and
    // the run's carryover when one had, so no client could tell which number it had been handed.
    // A screen reading it as "today's debt" showed −500 while the ledger below summed to −833.33.

    [Fact]
    public async Task A_statement_without_a_settled_run_reports_the_live_balance_and_no_snapshot()
    {
        var payeeId = await SeedPayeeAsync("STMT-NORUN");
        await SeedBalanceAsync(payeeId, 800m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        var response = await client.GetAsync($"/api/payees/{payeeId}/ledger/statement");
        var st = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.EnumerateArray().Single();

        st.GetProperty("currentBalance").GetDecimal().Should().Be(-800m, "the live balance is always there");
        st.GetProperty("newCarryover").ValueKind.Should().Be(JsonValueKind.Null,
            "there is no run to carry anything over from");
        st.GetProperty("previousDebt").ValueKind.Should().Be(JsonValueKind.Null);
        st.GetProperty("commissionsThisPeriod").ValueKind.Should().Be(JsonValueKind.Null,
            "zero would claim the payee earned nothing; the truth is no run has closed");
        st.GetProperty("settledAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task After_a_settled_run_the_statement_carries_BOTH_the_snapshot_and_the_live_balance()
    {
        // Rudolph's case, reproduced: a run settles, a debit lands afterwards, and the two figures
        // legitimately disagree. Both must be present so the screen can explain the gap.
        var payeeId = await SeedPayeeAsync("STMT-DRIFT");
        await SeedBalanceAsync(payeeId, 1500m);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var balance = await db.PayeeBalances.IgnoreQueryFilters().SingleAsync(b => b.PayeeId == payeeId);

            // The run withholds 1000 and carries 500 over.
            var applied = PayeeLedgerEntry.CreateSystemEntry(
                TestConstants.TenantA, payeeId, LedgerTransactionType.ClawbackAppliedCredit,
                Money.Of(1000m, "EUR"), "Withheld from pay run.", LedgerSourceType.PayRunSettlement,
                "system", Guid.NewGuid(), Now, Guid.NewGuid());
            balance.Apply(applied, Now);
            db.PayeeLedgerEntries.Add(applied);

            var runId = Guid.NewGuid();
            db.PayRuns.Add(PayRun.Open(TestConstants.TenantA, new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 31), "test", runId, Now, 0));
            db.PayRunSettlements.Add(PayRunSettlement.Record(
                TestConstants.TenantA, runId, payeeId,
                Money.Of(1000m, "EUR"), Money.Of(1000m, "EUR"), Money.Of(500m, "EUR"),
                applied.Id, "test", Guid.NewGuid(), Now));

            // …and THEN a churn debit lands, which the settled run knows nothing about.
            var late = PayeeLedgerEntry.CreateSystemEntry(
                TestConstants.TenantA, payeeId, LedgerTransactionType.ClawbackDebit,
                Money.Of(333.3333m, "EUR"), "A deal that synced later.", LedgerSourceType.DealChurn,
                "system", Guid.NewGuid(), Now.AddHours(1), Guid.NewGuid());
            balance.Apply(late, Now.AddHours(1));
            db.PayeeLedgerEntries.Add(late);

            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");
        var response = await client.GetAsync($"/api/payees/{payeeId}/ledger/statement");
        var st = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.EnumerateArray().Single();

        st.GetProperty("newCarryover").GetDecimal().Should().Be(-500m, "the run closed at −500 and that never changes");
        st.GetProperty("currentBalance").GetDecimal().Should().Be(-833.3333m, "the live balance moved after the run");
        st.GetProperty("settledAt").ValueKind.Should().NotBe(JsonValueKind.Null, "the photograph is dated");

        // The two figures differing IS the point — the screen turns this into a visible sentence.
        st.GetProperty("currentBalance").GetDecimal()
            .Should().NotBe(st.GetProperty("newCarryover").GetDecimal());
    }
}
