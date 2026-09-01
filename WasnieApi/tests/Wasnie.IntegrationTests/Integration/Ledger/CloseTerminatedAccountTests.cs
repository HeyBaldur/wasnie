using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Ledger;

/// <summary>
/// Closing a departed payee's account, end to end, against a real database.
///
/// ★★ WHAT THIS PROTECTS. The closure destroys a claim: it marks commission terminal and zeroes a
/// ledger balance, and neither can be undone — credits reach a final state and the ledger is
/// append-only. So the two things worth proving are that it closes EXACTLY what the user was shown,
/// and that a closed credit can never come back into a pay run
/// (docs/DIAG_ORPHAN_ACCOUNT_CLOSURE.md).
///
/// ★ AND WHY THE SET CHECK IS STRICT RATHER THAN A TOTAL. A departed payee's credit set is genuinely
/// unstable — the product deliberately allows a credit to arrive after someone leaves, and one already
/// did, 56 seconds after a termination. Two credits can also change while the sum stays put, which is
/// the case a total comparison would wave through and this suite pins.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class CloseTerminatedAccountTests(TestDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    // ★ EVERY CODE HERE IS PREFIXED "CTA-". The suites in this collection share ONE database, so an
    // employee code has to be unique across the whole assembly — plain names like "STILL-HERE" and
    // "RBAC-REP" already belonged to the settlement and endpoint suites, and the collision only shows
    // up when the full suite runs, never when this file runs alone.
    private HttpClient Finance() =>
        fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

    private sealed record Seeded(Guid PayeeId, List<(Guid Id, decimal Amount)> Credits);

    /// <summary>
    /// A departed payee with unpaid commission, and optionally a ledger debt.
    ///
    /// ★ NO PayeeBalance ROW UNLESS THERE IS DEBT. That absence is the ordinary case — the ledger
    /// records what someone OWES, so earned-and-unpaid commission leaves no balance behind — and it is
    /// the shape both real rows have.
    /// </summary>
    private async Task<Seeded> SeedAsync(
        string code, decimal[] creditAmounts, decimal debt = 0m, bool terminated = true)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenantId = TestConstants.TenantA;

        var payee = Payee.Create(tenantId, $"Payee {code}", code, $"{code}-{Guid.NewGuid():N}@test.com",
            new DateOnly(2020, 1, 1), "seed", Guid.NewGuid(), Now);
        if (terminated)
            payee.MarkAsTerminated(new DateOnly(2026, 6, 30), "hr@acme.com", Now);
        db.Payees.Add(payee);

        var planId = Guid.NewGuid();
        var plan = Plan.Create(tenantId, $"Plan {code}", "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), Eur,
            "seed", planId, Now, Guid.NewGuid());
        plan.AddRule("Tier 1: 4% up to quota", 1,
            new Measurement
            {
                Type = MeasurementType.Revenue,
                SourceField = "amount",
                Aggregation = MeasurementAggregation.Sum,
            },
            RateTable.Flat(0.04m));
        db.CompensationPlans.Add(plan);
        var ruleId = plan.Rules.First().Id;

        var credits = new List<(Guid, decimal)>();
        var i = 0;
        foreach (var amount in creditAmounts)
        {
            var tx = CompensationTransaction.Ingest(tenantId, $"{code}-TX{i++}", payee.Id,
                Money.Of(amount * 25m, Eur), new DateOnly(2026, 6, 16), TransactionSource.Manual,
                "seed", Guid.NewGuid(), Now, Guid.NewGuid());
            db.CompensationTransactions.Add(tx);

            var credit = Credit.Allocate(tenantId, tx.Id, payee.Id, planId, ruleId,
                RuleSnapshot.Freeze(ruleId, planId, 1, "Tier 1: 4% up to quota",
                    RateTable.Flat(0.04m), Trigger.Always(), Now),
                Money.Of(amount * 25m, Eur), Money.Of(amount, Eur),
                Percentage.FromPercent(100), CreditRole.Primary,
                "seed", Guid.NewGuid(), Now, Guid.NewGuid());
            db.Credits.Add(credit);
            credits.Add((credit.Id, amount));
        }

        if (debt != 0m)
        {
            var entry = PayeeLedgerEntry.CreateSystemEntry(
                tenantId, payee.Id, LedgerTransactionType.ClawbackDebit, Money.Of(Math.Abs(debt), Eur),
                "Churned deal.", LedgerSourceType.DealChurn, "system",
                Guid.NewGuid(), Now, Guid.NewGuid());
            var balance = PayeeBalance.Open(tenantId, payee.Id, Eur, Guid.NewGuid(), Now);
            balance.Apply(entry, Now);
            db.PayeeLedgerEntries.Add(entry);
            db.PayeeBalances.Add(balance);
        }

        await db.SaveChangesAsync();
        return new Seeded(payee.Id, credits);
    }

    private static object Body(
        Seeded seed, string resolution = "SettledExternally", decimal? expectedBalance = null,
        IEnumerable<(Guid Id, decimal Amount)>? credits = null) => new
    {
        currency = Eur,
        resolution,
        note = "Settled with the final paycheck.",
        credits = (credits ?? seed.Credits).Select(c => new { creditId = c.Id, amount = c.Amount }),
        expectedBalance,
    };

    private Task<HttpResponseMessage> CloseAsync(HttpClient client, Guid payeeId, object body) =>
        client.PostAsJsonAsync($"/api/payees/{payeeId}/ledger/close-account", body);

    private async Task<List<JsonElement>> QueueAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/payees/ledger/terminated-with-balance");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("rows").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    // ══ ★ The real case ═══════════════════════════════════════════════════════

    /// <summary>
    /// ★★ BIRGIT SCHNEIDER, REBUILT. €3,869.34 of commission, no ledger balance at all — the shape both
    /// real rows have. Closing takes her off the queue and leaves the credit terminal with its reason.
    /// </summary>
    [Fact]
    public async Task Closing_an_account_with_only_unpaid_commission_clears_it_from_the_queue()
    {
        var seed = await SeedAsync("CTA-BIRGIT", [3_869.34m]);
        var client = Finance();

        (await QueueAsync(client)).Should().Contain(r => r.GetProperty("payeeId").GetGuid() == seed.PayeeId);

        var response = await CloseAsync(client, seed.PayeeId, Body(seed));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await QueueAsync(client)).Should().NotContain(r => r.GetProperty("payeeId").GetGuid() == seed.PayeeId);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var credit = await db.Credits.IgnoreQueryFilters().SingleAsync(c => c.Id == seed.Credits[0].Id);

        credit.ClosedAt.Should().NotBeNull();
        credit.ClosureReason.Should().Be(CreditClosureReason.ExternalSettlement);
        credit.ClosureNote.Should().Be("Settled with the final paycheck.");
        credit.ConsumedAt.Should().BeNull("closing is not paying — no payout took this");
        credit.SupersededAt.Should().BeNull("closing is not replacing — nothing took its place");

        var payee = await db.Payees.IgnoreQueryFilters().SingleAsync(p => p.Id == seed.PayeeId);
        payee.AccountClosedAt.Should().NotBeNull();
    }

    /// <summary>Debt AND commission are one decision, resolved in one call.</summary>
    [Fact]
    public async Task Both_halves_are_resolved_together()
    {
        var seed = await SeedAsync("CTA-BOTH", [500m], debt: 800m);
        var client = Finance();

        var response = await CloseAsync(client, seed.PayeeId,
            Body(seed, expectedBalance: -800m));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await db.Credits.IgnoreQueryFilters().SingleAsync(c => c.Id == seed.Credits[0].Id))
            .ClosedAt.Should().NotBeNull();

        var balance = await db.PayeeBalances.IgnoreQueryFilters().SingleAsync(b => b.PayeeId == seed.PayeeId);
        balance.Balance.Amount.Should().Be(0m, "the debt was brought to zero in the same transaction");

        // ★ The TYPED entry, never a generic adjustment: a CFO totals "recovered elsewhere" apart from
        // "we ate the loss" without reading anybody's note.
        var entries = await db.PayeeLedgerEntries.IgnoreQueryFilters()
            .Where(e => e.PayeeId == seed.PayeeId).ToListAsync();
        entries.Should().ContainSingle(e =>
            e.TransactionType == LedgerTransactionType.ExternalSettlementCredit);
    }

    [Fact]
    public async Task A_write_off_uses_the_write_off_type_and_reason()
    {
        var seed = await SeedAsync("CTA-WO", [250m], debt: 400m);
        var client = Finance();

        (await CloseAsync(client, seed.PayeeId, Body(seed, "WrittenOff", expectedBalance: -400m)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await db.Credits.IgnoreQueryFilters().SingleAsync(c => c.Id == seed.Credits[0].Id))
            .ClosureReason.Should().Be(CreditClosureReason.WrittenOff);
        (await db.PayeeLedgerEntries.IgnoreQueryFilters()
            .Where(e => e.PayeeId == seed.PayeeId).ToListAsync())
            .Should().ContainSingle(e => e.TransactionType == LedgerTransactionType.WriteOffCredit);
    }

    // ══ ★★ Strict set concurrency ═════════════════════════════════════════════

    [Fact]
    public async Task A_credit_whose_amount_changed_is_a_conflict()
    {
        var seed = await SeedAsync("CTA-CONF-AMOUNT", [500m]);
        var client = Finance();

        var tampered = new[] { (seed.Credits[0].Id, 400m) };
        var response = await CloseAsync(client, seed.PayeeId, Body(seed, credits: tampered));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("CreditAmountChanged");
        await NothingWasWrittenAsync(seed);
    }

    [Fact]
    public async Task A_credit_the_payload_did_not_include_is_a_conflict()
    {
        var seed = await SeedAsync("CTA-CONF-APPEAR", [500m, 300m]);
        var client = Finance();

        // The user saw one of the two — the other arrived while the window was open.
        var partial = new[] { seed.Credits[0] };
        var response = await CloseAsync(client, seed.PayeeId, Body(seed, credits: partial));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("CreditAppeared");
        await NothingWasWrittenAsync(seed);
    }

    /// <summary>
    /// ★★ THE CASE A TOTAL COMPARISON WOULD WAVE THROUGH. Same count, same sum, different credits —
    /// one id in the payload does not exist on the account, and one that does was not offered.
    /// </summary>
    [Fact]
    public async Task A_set_that_sums_the_same_but_is_a_different_set_is_a_conflict()
    {
        var seed = await SeedAsync("CTA-CONF-SWAP", [500m, 300m]);
        var client = Finance();

        var swapped = new[] { seed.Credits[0], (Guid.NewGuid(), 300m) };
        var response = await CloseAsync(client, seed.PayeeId, Body(seed, credits: swapped));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("AccountSnapshotStale",
            "the totals match exactly — only the identities differ");
        await NothingWasWrittenAsync(seed);
    }

    [Fact]
    public async Task A_balance_that_moved_is_a_conflict()
    {
        var seed = await SeedAsync("CTA-CONF-BAL", [100m], debt: 900m);
        var client = Finance();

        var response = await CloseAsync(client, seed.PayeeId, Body(seed, expectedBalance: -500m));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("BalanceChanged");
        await NothingWasWrittenAsync(seed);
    }

    private async Task NothingWasWrittenAsync(Seeded seed)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await db.Credits.IgnoreQueryFilters().Where(c => c.PayeeId == seed.PayeeId).ToListAsync())
            .Should().OnlyContain(c => c.ClosedAt == null, "a refused closure closes nothing");
        (await db.Payees.IgnoreQueryFilters().SingleAsync(p => p.Id == seed.PayeeId))
            .AccountClosedAt.Should().BeNull();
    }

    // ══ Fail-closed ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task An_active_payee_cannot_have_their_account_closed()
    {
        var seed = await SeedAsync("CTA-STILL-HERE", [200m], terminated: false);

        (await CloseAsync(Finance(), seed.PayeeId, Body(seed)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await NothingWasWrittenAsync(seed);
    }

    /// <summary>Two clicks do not write two closures.</summary>
    [Fact]
    public async Task An_already_closed_account_cannot_be_closed_again()
    {
        var seed = await SeedAsync("CTA-TWICE", [150m]);
        var client = Finance();

        (await CloseAsync(client, seed.PayeeId, Body(seed))).StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await CloseAsync(client, seed.PayeeId, Body(seed));
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.PayeeLedgerEntries.IgnoreQueryFilters()
            .CountAsync(e => e.PayeeId == seed.PayeeId))
            .Should().Be(0, "there was no debt, so no entry — and certainly not two");
    }

    // ══ Permission ════════════════════════════════════════════════════════════

    /// <summary>
    /// ★ Ledger.Read opens the queue; it must not close an account. A Manager holds the first and not
    /// the second, which is exactly the separation the new permission exists for.
    /// </summary>
    [Theory]
    [InlineData("Rep")]
    [InlineData("Manager")]
    public async Task A_role_without_the_closing_permission_is_refused_and_writes_nothing(string role)
    {
        var seed = await SeedAsync($"CTA-RBAC-{role}", [700m]);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: role);

        (await CloseAsync(client, seed.PayeeId, Body(seed)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await NothingWasWrittenAsync(seed);
    }

    [Fact]
    public async Task Closing_requires_a_token()
    {
        var seed = await SeedAsync("CTA-RBAC-ANON", [700m]);

        (await CloseAsync(fixture.Factory.CreateClient(), seed.PayeeId, Body(seed)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ══ ★ Money-closed: a closed credit never comes back ══════════════════════

    /// <summary>
    /// ★★ THE ONE THAT MATTERS MOST. A written-off credit re-entering a pay run is a second payment of
    /// money the company decided not to pay. The engine filters on the three nulls together, so this
    /// asserts the closure actually reaches the calculation and not only the screen.
    /// </summary>
    [Fact]
    public async Task A_closed_credit_never_enters_a_pay_run()
    {
        var seed = await SeedAsync("CTA-NO-REPAY", [1_000m]);
        var client = Finance();

        (await CloseAsync(client, seed.PayeeId, Body(seed, "WrittenOff")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Straight at the engine's own predicate — the credit must not be selectable as outstanding.
        var outstanding = await db.Credits.IgnoreQueryFilters()
            .Where(c => c.PayeeId == seed.PayeeId
                     && c.SupersededAt == null
                     && c.ConsumedAt == null
                     && c.ClosedAt == null)
            .ToListAsync();

        outstanding.Should().BeEmpty("a closed credit is terminal and the engine must not see it");
    }

    // ══ The audit row ═════════════════════════════════════════════════════════

    /// <summary>
    /// ★ THE IDS AND THE AMOUNTS. The diagnosis found that a closure would otherwise be recorded as a
    /// flag and a paragraph, leaving "what happened to this money" answerable only by reading prose.
    /// </summary>
    [Fact]
    public async Task The_audit_row_names_the_credits_that_were_closed()
    {
        var seed = await SeedAsync("CTA-AUDIT", [3_869.34m]);

        (await CloseAsync(Finance(), seed.PayeeId, Body(seed))).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var log = await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.ResourceId == seed.PayeeId.ToString())
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();

        log.Should().NotBeNull();
        log!.Action.Should().Be("TERMINATED_ACCOUNT_CLOSED");
        log.Metadata.Should().NotBeNull();
        log.Metadata!.Should().Contain(seed.Credits[0].Id.ToString(), "the credit id is in the row");
        log.Metadata.Should().Contain("3869.34", "and so is what it was worth");
        log.Metadata.Should().Contain("SettledExternally");
    }

    // ══ Scoping ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Another_tenant_cannot_close_this_account()
    {
        var seed = await SeedAsync("CTA-TENANT-A", [600m]);
        var other = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB, role: "CompManager");

        var response = await CloseAsync(other, seed.PayeeId, Body(seed));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the payee is not visible to them");
        await NothingWasWrittenAsync(seed);
    }
}
