using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
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

    /// <summary>
    /// The queue's rows. The endpoint answers with an OBJECT now — rows plus per-currency totals —
    /// because the totals are money and a screen must not add money up itself.
    /// </summary>
    private async Task<List<JsonElement>> ListAsync(HttpClient client)
    {
        return (await ReadQueueAsync(client))
            .GetProperty("rows").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private async Task<JsonElement> ReadQueueAsync(HttpClient client)
    {
        var response = await client.GetAsync(Endpoint);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
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

    // ══ The other direction: a departed payee the company OWES ═══════════════
    // Terminated payees are excluded from every pay run, so a positive balance can never be paid by
    // the engine — it would sit there forever. Treasury pays it outside Wasnie; this entry records it.

    [Fact]
    public async Task Paying_a_departed_payee_in_credit_takes_the_balance_to_absolute_zero()
    {
        var payeeId = await SeedPayeeAsync("LEFT-OWED", terminated: true, balance: 0m);

        // Put them in credit the way it actually happens: a correction that outlives the withholding.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var balance = await db.PayeeBalances.IgnoreQueryFilters().SingleAsync(b => b.PayeeId == payeeId);
            var credit = PayeeLedgerEntry.CreateManualAdjustment(
                TestConstants.TenantA, payeeId, LedgerTransactionType.DataCorrectionCredit,
                Money.Of(500m, Eur), "Technical correction — the withheld debt was not real.",
                "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid());
            balance.Apply(credit, Now);
            db.PayeeLedgerEntries.Add(credit);
            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");
        (await ListAsync(client)).Should().Contain(r => r.GetProperty("payeeId").GetGuid() == payeeId,
            "a departed payee the company owes is an open account too");

        var post = await client.PostAsJsonAsync($"/api/payees/{payeeId}/ledger/adjustments", new
        {
            transactionType = "FinalSettlementDebit",
            amount = 500m,
            currency = Eur,
            justification = "Treasury transferred the outstanding balance with the final paycheck.",
        });
        post.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verify = fixture.Factory.Services.CreateScope();
        var vdb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var after = await vdb.PayeeBalances.IgnoreQueryFilters().SingleAsync(b => b.PayeeId == payeeId);

        after.Balance.Amount.Should().Be(0.0000m, "absolute zero — the account is closed, not nearly closed");
        after.OutstandingDebt().Amount.Should().Be(0m);

        // Append-only: the credit that put them in the black is untouched next to the payment.
        var entries = await vdb.PayeeLedgerEntries.IgnoreQueryFilters()
            .Where(e => e.PayeeId == payeeId).ToListAsync();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.TransactionType == LedgerTransactionType.DataCorrectionCredit
                                   && e.Amount.Amount == 500m);
        var payment = entries.Single(e => e.TransactionType == LedgerTransactionType.FinalSettlementDebit);
        payment.Amount.Amount.Should().Be(-500m, "the sign comes from the type: cash left the company");
        payment.Origin.Should().Be(LedgerEntryOrigin.Human);
        entries.Sum(e => e.Amount.Amount).Should().Be(0m, "the ledger closes");

        (await ListAsync(client)).Should().NotContain(r => r.GetProperty("payeeId").GetGuid() == payeeId);
    }

    [Fact]
    public async Task Settling_a_departed_payee_does_not_pull_them_back_into_a_pay_run()
    {
        // The whole reason this is a manual entry and not an engine feature: closing the account must
        // not look like a payment event. No payout, no settlement row, no netting — the payee stays
        // excluded, exactly as terminating them decided.
        var payeeId = await SeedPayeeAsync("LEFT-NOSIDE", terminated: true, balance: 0m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var balance = await db.PayeeBalances.IgnoreQueryFilters().SingleAsync(b => b.PayeeId == payeeId);
            var credit = PayeeLedgerEntry.CreateManualAdjustment(
                TestConstants.TenantA, payeeId, LedgerTransactionType.ManualBonusCredit,
                Money.Of(300m, Eur), "Owed on departure.", "finance@acme.com",
                Guid.NewGuid(), Now, Guid.NewGuid());
            balance.Apply(credit, Now);
            db.PayeeLedgerEntries.Add(credit);
            await db.SaveChangesAsync();
        }

        await client.PostAsJsonAsync($"/api/payees/{payeeId}/ledger/adjustments", new
        {
            transactionType = "FinalSettlementDebit", amount = 300m, currency = Eur,
            justification = "Paid out on termination.",
        });

        using var verify = fixture.Factory.Services.CreateScope();
        var vdb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await vdb.CompensationPayouts.IgnoreQueryFilters().CountAsync(p => p.PayeeId == payeeId))
            .Should().Be(0, "settling an account is not a payout");
        (await vdb.PayRunSettlements.IgnoreQueryFilters().CountAsync(s => s.PayeeId == payeeId))
            .Should().Be(0, "no pay run settled anything — there was no run");
        (await vdb.PayeeLedgerEntries.IgnoreQueryFilters()
            .CountAsync(e => e.PayeeId == payeeId
                          && e.TransactionType == LedgerTransactionType.ClawbackAppliedCredit))
            .Should().Be(0, "no netting was triggered");

        // And the payee is still terminated: the closing entry changed money, not employment.
        (await vdb.Payees.IgnoreQueryFilters().SingleAsync(p => p.Id == payeeId))
            .Status.Should().Be(PayeeStatus.Terminated);
    }

    [Fact]
    public async Task A_final_settlement_that_would_overshoot_is_refused_over_HTTP_and_writes_nothing()
    {
        // The amount is typed by a person. A typo — €600 against +€500 — would flip the balance to
        // −€100 and invent a debt against someone who already left, which then shows up on THIS very
        // screen as an open account waiting for a write-off. The domain refuses before SaveChanges,
        // so the API path leaves no trace at all.
        var payeeId = await SeedPayeeAsync("LEFT-TYPO", terminated: true, balance: 0m);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var balance = await db.PayeeBalances.IgnoreQueryFilters().SingleAsync(b => b.PayeeId == payeeId);
            var credit = PayeeLedgerEntry.CreateManualAdjustment(
                TestConstants.TenantA, payeeId, LedgerTransactionType.DataCorrectionCredit,
                Money.Of(500m, Eur), "Technical correction — the withheld debt was not real.",
                "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid());
            balance.Apply(credit, Now);
            db.PayeeLedgerEntries.Add(credit);
            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");
        var post = await client.PostAsJsonAsync($"/api/payees/{payeeId}/ledger/adjustments", new
        {
            transactionType = "FinalSettlementDebit",
            amount = 600m,
            currency = Eur,
            justification = "Treasury transferred the outstanding balance.",
        });

        post.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var verify = fixture.Factory.Services.CreateScope();
        var vdb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var after = await vdb.PayeeBalances.IgnoreQueryFilters().SingleAsync(b => b.PayeeId == payeeId);
        after.Balance.Amount.Should().Be(500m, "the balance is exactly as it was");
        after.OutstandingDebt().Amount.Should().Be(0m, "no fictitious debt was opened");

        (await vdb.PayeeLedgerEntries.IgnoreQueryFilters()
            .CountAsync(e => e.PayeeId == payeeId
                          && e.TransactionType == LedgerTransactionType.FinalSettlementDebit))
            .Should().Be(0, "the rejected entry was never persisted");
    }

    [Fact]
    public async Task A_rep_cannot_pay_out_a_departed_payee_either()
    {
        var payeeId = await SeedPayeeAsync("RBAC-FINAL", terminated: true, balance: 0m);
        var rep = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Rep");

        var response = await rep.PostAsJsonAsync($"/api/payees/{payeeId}/ledger/adjustments", new
        {
            transactionType = "FinalSettlementDebit", amount = 500m, currency = Eur,
            justification = "Trying to pay myself out.",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // == The half that was invisible: commission earned and never paid ========
    //
    // The ledger records what a payee OWES. Commission they EARNED and were never paid produces NO
    // PayeeBalance row at all, so a queue built on balances read "nothing outstanding" for someone owed
    // real money - while the pay run skipped them for being terminated. Invisible on both sides at once
    // (docs/DIAG_POL-8554_PAYOUT_Y_CREDITOS_INVENTADOS.md). These tests are that hole.

    /// <summary>
    /// Seeds a departed payee holding one commission credit, and nothing else - deliberately with NO
    /// PayeeBalance row, because that absence is the exact shape of the bug.
    /// </summary>
    private async Task<(Guid PayeeId, Guid CreditId, Guid TransactionId, string Reference)>
        SeedPayeeWithUnsettledCreditAsync(
            string code, decimal commission, bool terminated = true, bool consumed = false)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenantId = TestConstants.TenantA;

        var payee = Payee.Create(tenantId, $"Payee {code}", code, $"{code}-{Guid.NewGuid():N}@test.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        if (terminated)
            payee.MarkAsTerminated(new DateOnly(2026, 6, 30), "hr@acme.com", Now);
        db.Payees.Add(payee);

        var planId = Guid.NewGuid();
        var plan = Plan.Create(tenantId, $"Plan {code}", "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), Eur,
            "test", planId, Now, Guid.NewGuid());
        plan.AddRule("Tier 1: 4% up to quota", 1,
            new Measurement
            {
                Type = MeasurementType.Revenue,
                SourceField = "amount",
                Aggregation = MeasurementAggregation.Sum,
            },
            RateTable.Flat(0.04m));
        db.CompensationPlans.Add(plan);

        var reference = $"REF-{code}";
        var tx = CompensationTransaction.Ingest(tenantId, reference, payee.Id,
            Money.Of(commission * 25m, Eur), new DateOnly(2026, 6, 16), TransactionSource.Manual,
            "test", Guid.NewGuid(), Now, Guid.NewGuid());
        db.CompensationTransactions.Add(tx);

        var ruleId = plan.Rules.First().Id;
        var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Tier 1: 4% up to quota",
            RateTable.Flat(0.04m), Trigger.Always(), Now);

        var credit = Credit.Allocate(tenantId, tx.Id, payee.Id, planId, ruleId, snapshot,
            Money.Of(commission * 25m, Eur), Money.Of(commission, Eur),
            Percentage.FromPercent(100), CreditRole.Primary,
            "test", Guid.NewGuid(), Now, Guid.NewGuid());

        if (consumed)
            credit.Consume(Guid.NewGuid(), Now, Guid.NewGuid());

        db.Credits.Add(credit);

        await db.SaveChangesAsync();
        return (payee.Id, credit.Id, tx.Id, reference);
    }

    /// <summary>
    /// * THE CASE THAT WAS INVISIBLE. A departed payee with unpaid commission and NO balance row at all.
    /// The old query started from PayeeBalances, so "no row" meant "settled" and the money vanished.
    /// </summary>
    [Fact]
    public async Task Unpaid_commission_puts_a_departed_payee_in_the_queue_with_no_balance_row_at_all()
    {
        var seed = await SeedPayeeWithUnsettledCreditAsync("GONE-UNPAID", 3869.34m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.PayeeBalances.IgnoreQueryFilters().AnyAsync(b => b.PayeeId == seed.PayeeId))
                .Should().BeFalse("the premise of this test is that the ledger has nothing on this person");
        }

        var row = (await ListAsync(client)).Single(r => r.GetProperty("payeeId").GetGuid() == seed.PayeeId);

        row.GetProperty("unsettledCreditTotal").GetDecimal().Should().Be(3869.34m);
        row.GetProperty("currency").GetString().Should().Be(Eur);
        row.GetProperty("balance").GetDecimal().Should().Be(0m, "there is no ledger balance to report");
        row.GetProperty("balanceUpdatedAt").ValueKind.Should().Be(JsonValueKind.Null,
            "no balance row is a different fact from a balance updated to zero");
    }

    /// <summary>
    /// A total is something to worry about; the plan, the rule, the date and the sale are what somebody
    /// can act on. The row carries all four.
    /// </summary>
    [Fact]
    public async Task Each_unpaid_credit_carries_what_a_person_needs_to_act_on_it()
    {
        var seed = await SeedPayeeWithUnsettledCreditAsync("GONE-DETAIL", 500m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        var row = (await ListAsync(client)).Single(r => r.GetProperty("payeeId").GetGuid() == seed.PayeeId);
        var credit = row.GetProperty("unsettledCredits").EnumerateArray().Single();

        credit.GetProperty("creditId").GetGuid().Should().Be(seed.CreditId);
        credit.GetProperty("amount").GetDecimal().Should().Be(500m);
        credit.GetProperty("planName").GetString().Should().Be("Plan GONE-DETAIL");
        credit.GetProperty("ruleName").GetString().Should().Be("Tier 1: 4% up to quota");
        credit.GetProperty("allocatedAt").GetString().Should().Be("2026-07-29");
        credit.GetProperty("transactionId").GetGuid().Should().Be(seed.TransactionId);
        credit.GetProperty("transactionReference").GetString().Should().Be(seed.Reference);
    }

    /// <summary>Paid is paid. A credit a payout consumed is finished work, not queue work.</summary>
    [Fact]
    public async Task A_credit_already_consumed_by_a_payout_is_not_in_the_queue()
    {
        var seed = await SeedPayeeWithUnsettledCreditAsync("GONE-PAID", 750m, consumed: true);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        (await ListAsync(client))
            .Should().NotContain(r => r.GetProperty("payeeId").GetGuid() == seed.PayeeId);
    }

    /// <summary>
    /// The credit half must not widen the queue to people who are still here. Their commission is not
    /// orphaned - the next pay run picks it up, which is precisely what termination stops.
    /// </summary>
    [Fact]
    public async Task An_active_payee_with_unpaid_commission_is_not_in_the_queue()
    {
        var seed = await SeedPayeeWithUnsettledCreditAsync("HERE-UNPAID", 900m, terminated: false);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        (await ListAsync(client))
            .Should().NotContain(r => r.GetProperty("payeeId").GetGuid() == seed.PayeeId);
    }

    /// <summary>The total is the sum of the rows, computed by the server, and per currency.</summary>
    [Fact]
    public async Task The_total_is_the_sum_of_the_rows_and_never_blends_currencies()
    {
        var a = await SeedPayeeWithUnsettledCreditAsync("TOT-A", 1000m);
        var b = await SeedPayeeWithUnsettledCreditAsync("TOT-B", 250.50m);
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        var queue = await ReadQueueAsync(client);
        var rows = queue.GetProperty("rows").EnumerateArray()
            .Where(r => r.GetProperty("payeeId").GetGuid() == a.PayeeId
                     || r.GetProperty("payeeId").GetGuid() == b.PayeeId)
            .ToList();

        rows.Sum(r => r.GetProperty("unsettledCreditTotal").GetDecimal()).Should().Be(1250.50m);

        var eur = queue.GetProperty("totals").EnumerateArray()
            .Single(t => t.GetProperty("currency").GetString() == Eur);

        // Every total is one currency's own. The seeds above are all EUR, so this row must at least
        // cover them; other tests in the suite add their own EUR rows, hence >= rather than ==.
        eur.GetProperty("unsettledCreditTotal").GetDecimal().Should().BeGreaterThanOrEqualTo(1250.50m);
        queue.GetProperty("totals").EnumerateArray()
            .Select(t => t.GetProperty("currency").GetString())
            .Should().OnlyHaveUniqueItems("one total per currency, never a blended figure");
    }

    /// <summary>Scoping: the queue is per tenant, like everything else that touches money.</summary>
    [Fact]
    public async Task The_queue_never_shows_another_tenants_unpaid_commission()
    {
        var seed = await SeedPayeeWithUnsettledCreditAsync("GONE-TENANT-A", 4200m);
        var otherTenant = fixture.Factory.CreateClient()
            .WithAuth(TestConstants.TenantB, role: "CompManager");

        var body = await (await otherTenant.GetAsync(Endpoint)).Content.ReadAsStringAsync();
        body.Should().NotContain(seed.PayeeId.ToString());
        body.Should().NotContain(seed.Reference);
    }
}
