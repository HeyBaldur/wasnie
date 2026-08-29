using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Handlers.Ledger;
using Wasnie.Application.Compensation.Queries.Ledger;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The work queue for departed payees, and the hole this handler was rewritten to close.
///
/// It used to start from <c>PayeeBalances</c>. The ledger records what a payee OWES, so commission they
/// EARNED and were never paid produces no balance row at all — and a queue driven by balances therefore
/// reported "nothing outstanding" about someone owed real money, while the pay run skipped them for
/// being terminated. Invisible on both sides at once
/// (docs/DIAG_POL-8554_PAYOUT_Y_CREDITOS_INVENTADOS.md).
///
/// Every test below is about that: which facts can put a row on the list, and which must not.
/// </summary>
public sealed class TerminatedAccountsQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";
    private const string Usd = "USD";

    private sealed record Harness(
        ApplicationDbContext Db,
        ListTerminatedPayeesWithBalanceHandler Handler,
        Guid TenantId);

    private static Harness Build(string dbName, PayeeVisibility? visibility = null)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var guard = Substitute.For<IPayeeAccessGuard>();
        guard.GetVisibilityAsync(Arg.Any<CancellationToken>())
            .Returns(visibility ?? PayeeVisibility.Everything);

        var handler = new ListTerminatedPayeesWithBalanceHandler(
            db, Substitute.For<IAuthorizationService>(), guard);

        return new Harness(db, handler, tenantId);
    }

    // ── Seeding ───────────────────────────────────────────────────────────────
    // Deliberately granular: the point of most tests here is which pieces are PRESENT, and a single
    // do-everything seeder would hide the absence that is the actual subject.

    private static Payee AddPayee(Harness h, string code, bool terminated)
    {
        var payee = Payee.Create(h.TenantId, $"Payee {code}", code, $"{code}@acme.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        if (terminated)
            payee.MarkAsTerminated(new DateOnly(2026, 6, 30), "hr@acme.com", Now);

        h.Db.Payees.Add(payee);
        h.Db.SaveChanges();
        return payee;
    }

    /// <summary>A ledger debt, which also creates the balance row that used to be the only entry ticket.</summary>
    private static void AddDebt(Harness h, Guid payeeId, decimal amount, string currency = Eur)
    {
        var entry = PayeeLedgerEntry.CreateSystemEntry(
            h.TenantId, payeeId, LedgerTransactionType.ClawbackDebit, Money.Of(amount, currency),
            "Churned deal.", LedgerSourceType.DealChurn, "system",
            Guid.NewGuid(), Now, Guid.NewGuid());
        var balance = PayeeBalance.Open(h.TenantId, payeeId, currency, Guid.NewGuid(), Now);
        balance.Apply(entry, Now);

        h.Db.PayeeLedgerEntries.Add(entry);
        h.Db.PayeeBalances.Add(balance);
        h.Db.SaveChanges();
    }

    /// <summary>
    /// Commission earned. ★ Note what is NOT written: no ledger entry and no PayeeBalance row. That
    /// absence is not a shortcut in the test — it is exactly what the product stores, and it is the
    /// reason this money was invisible.
    /// </summary>
    private static Credit AddUnsettledCredit(
        Harness h, Guid payeeId, decimal commission, string currency = Eur,
        string reference = "POL-1", bool consumed = false, bool superseded = false)
    {
        var planId = Guid.NewGuid();
        var plan = Plan.Create(h.TenantId, $"Plan for {reference}", "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), currency,
            "test", planId, Now, Guid.NewGuid());
        plan.AddRule("Tier 1: 4% up to quota", 1,
            new Measurement
            {
                Type = MeasurementType.Revenue,
                SourceField = "amount",
                Aggregation = MeasurementAggregation.Sum,
            },
            RateTable.Flat(0.04m));
        h.Db.CompensationPlans.Add(plan);

        var tx = CompensationTransaction.Ingest(h.TenantId, reference, payeeId,
            Money.Of(commission * 25m, currency), new DateOnly(2026, 6, 16),
            TransactionSource.Manual, "test", Guid.NewGuid(), Now, Guid.NewGuid());
        h.Db.CompensationTransactions.Add(tx);

        var ruleId = plan.Rules.First().Id;
        var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Tier 1: 4% up to quota",
            RateTable.Flat(0.04m), Trigger.Always(), Now);

        var credit = Credit.Allocate(h.TenantId, tx.Id, payeeId, planId, ruleId, snapshot,
            Money.Of(commission * 25m, currency), Money.Of(commission, currency),
            Percentage.FromPercent(100), CreditRole.Primary,
            "test", Guid.NewGuid(), Now, Guid.NewGuid());

        if (superseded)
            credit.Supersede("Reassigned to another payee.", Now, Guid.NewGuid());
        if (consumed)
            credit.Consume(Guid.NewGuid(), Now, Guid.NewGuid());

        h.Db.Credits.Add(credit);
        h.Db.SaveChanges();
        return credit;
    }

    private static Task<Wasnie.Domain.Common.Results.Result<
        Wasnie.Application.Compensation.DTOs.TerminatedAccountsDto>> RunAsync(Harness h) =>
        h.Handler.Handle(new ListTerminatedPayeesWithBalanceQuery(), CancellationToken.None);

    // ══ The hole ══════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ THE CASE THAT WAS INVISIBLE, and the reason this handler exists in its new shape. A departed
    /// payee owed commission, with NO ledger balance row anywhere. The old query started from
    /// PayeeBalances, so "no row" read as "settled" and €3,869.34 disappeared from the product.
    /// </summary>
    [Fact]
    public async Task Unpaid_commission_puts_a_departed_payee_on_the_list_with_no_balance_row_at_all()
    {
        var h = Build(nameof(Unpaid_commission_puts_a_departed_payee_on_the_list_with_no_balance_row_at_all));
        var payee = AddPayee(h, "GONE", terminated: true);
        AddUnsettledCredit(h, payee.Id, 3869.34m, reference: "POL-8554");

        h.Db.PayeeBalances.Should().BeEmpty("the premise is that the ledger holds nothing on this person");

        var result = await RunAsync(h);

        var row = result.Value!.Rows.Should().ContainSingle().Subject;
        row.PayeeId.Should().Be(payee.Id);
        row.EmployeeCode.Should().Be("GONE");
        row.UnsettledCreditTotal.Should().Be(3869.34m);
        row.Balance.Should().Be(0m, "there is no ledger balance to report");
        row.BalanceUpdatedAt.Should().BeNull(
            "no balance row is a different fact from a balance updated to zero");
    }

    /// <summary>
    /// Every row carries what a person needs to act: who, how much, under which plan and rule, when it
    /// was credited, and the sale it came from. A bare total is worry, not work.
    /// </summary>
    [Fact]
    public async Task Each_unpaid_credit_names_its_plan_rule_date_and_source_transaction()
    {
        var h = Build(nameof(Each_unpaid_credit_names_its_plan_rule_date_and_source_transaction));
        var payee = AddPayee(h, "GONE-DETAIL", terminated: true);
        var credit = AddUnsettledCredit(h, payee.Id, 500m, reference: "POL-77");

        var result = await RunAsync(h);

        var line = result.Value!.Rows.Single().UnsettledCredits.Should().ContainSingle().Subject;
        line.CreditId.Should().Be(credit.Id);
        line.Amount.Should().Be(500m);
        line.Currency.Should().Be(Eur);
        line.PlanName.Should().Be("Plan for POL-77");
        line.RuleName.Should().Be("Tier 1: 4% up to quota");
        line.AllocatedAt.Should().Be(new DateOnly(2026, 8, 28));
        line.TransactionReference.Should().Be("POL-77");
    }

    // ══ What must NOT widen the queue ═════════════════════════════════════════

    /// <summary>Paid is paid. A credit a payout consumed is finished work, not queue work.</summary>
    [Fact]
    public async Task A_credit_already_consumed_by_a_payout_is_not_on_the_list()
    {
        var h = Build(nameof(A_credit_already_consumed_by_a_payout_is_not_on_the_list));
        var payee = AddPayee(h, "GONE-PAID", terminated: true);
        AddUnsettledCredit(h, payee.Id, 750m, consumed: true);

        (await RunAsync(h)).Value!.Rows.Should().BeEmpty();
    }

    /// <summary>
    /// A superseded credit was REPLACED — typically because the sale was reattributed to someone else.
    /// Showing it would invite paying a commission that a live credit already covers, for a different
    /// person. Same pair of nulls the pay-run engine filters on, so the queue cannot drift from it.
    /// </summary>
    [Fact]
    public async Task A_superseded_credit_is_not_on_the_list()
    {
        var h = Build(nameof(A_superseded_credit_is_not_on_the_list));
        var payee = AddPayee(h, "GONE-STALE", terminated: true);
        AddUnsettledCredit(h, payee.Id, 5999m, superseded: true);

        (await RunAsync(h)).Value!.Rows.Should().BeEmpty();
    }

    /// <summary>
    /// The credit half must not widen the queue to people who are still here. Their commission is not
    /// orphaned — the next pay run picks it up, which is precisely what termination stops.
    /// </summary>
    [Fact]
    public async Task An_active_payee_with_unpaid_commission_is_not_on_the_list()
    {
        var h = Build(nameof(An_active_payee_with_unpaid_commission_is_not_on_the_list));
        var payee = AddPayee(h, "STILL-HERE", terminated: false);
        AddUnsettledCredit(h, payee.Id, 900m);

        (await RunAsync(h)).Value!.Rows.Should().BeEmpty();
    }

    /// <summary>A departed payee with nothing open is not work, and the queue is work.</summary>
    [Fact]
    public async Task A_departed_payee_with_no_debt_and_no_unpaid_commission_produces_no_row()
    {
        var h = Build(nameof(A_departed_payee_with_no_debt_and_no_unpaid_commission_produces_no_row));
        AddPayee(h, "GONE-CLEAN", terminated: true);

        var result = await RunAsync(h);
        result.Value!.Rows.Should().BeEmpty();
        result.Value.Totals.Should().BeEmpty();
    }

    // ══ The two halves coexist ════════════════════════════════════════════════

    /// <summary>
    /// The ledger half still works exactly as before — this rewrite must not trade one blind spot for
    /// another. A debt with no credits keeps its row, its sign and its updated-at.
    /// </summary>
    [Fact]
    public async Task A_ledger_debt_alone_still_puts_a_departed_payee_on_the_list()
    {
        var h = Build(nameof(A_ledger_debt_alone_still_puts_a_departed_payee_on_the_list));
        var payee = AddPayee(h, "GONE-OWING", terminated: true);
        AddDebt(h, payee.Id, 500m);

        var row = (await RunAsync(h)).Value!.Rows.Should().ContainSingle().Subject;
        row.Balance.Should().Be(-500m, "signed exactly as stored");
        row.BalanceUpdatedAt.Should().NotBeNull();
        row.UnsettledCredits.Should().BeEmpty();
    }

    /// <summary>
    /// ★ ONE ROW, TWO FACTS, NEVER ADDED. Someone can owe the company money AND be owed unpaid
    /// commission at the same time. Netting them would produce a figure describing neither, and would
    /// silently decide a set-off that is a person's call — so both are reported side by side.
    /// </summary>
    [Fact]
    public async Task A_debt_and_unpaid_commission_share_one_row_and_are_never_netted()
    {
        var h = Build(nameof(A_debt_and_unpaid_commission_share_one_row_and_are_never_netted));
        var payee = AddPayee(h, "GONE-BOTH", terminated: true);
        AddDebt(h, payee.Id, 500m);
        AddUnsettledCredit(h, payee.Id, 300m);

        var row = (await RunAsync(h)).Value!.Rows.Should().ContainSingle().Subject;
        row.Balance.Should().Be(-500m);
        row.UnsettledCreditTotal.Should().Be(300m, "not -200: the two are not one number");
    }

    // ══ Totals ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task The_total_is_the_sum_of_the_rows()
    {
        var h = Build(nameof(The_total_is_the_sum_of_the_rows));
        var a = AddPayee(h, "TOT-A", terminated: true);
        var b = AddPayee(h, "TOT-B", terminated: true);
        AddUnsettledCredit(h, a.Id, 1000m, reference: "POL-A1");
        AddUnsettledCredit(h, a.Id, 250.50m, reference: "POL-A2");
        AddUnsettledCredit(h, b.Id, 119.50m, reference: "POL-B1");

        var result = await RunAsync(h);

        var total = result.Value!.Totals.Should().ContainSingle().Subject;
        total.Currency.Should().Be(Eur);
        total.UnsettledCreditTotal.Should().Be(1370.00m);
        total.UnsettledCreditCount.Should().Be(3);
        total.PayeeCount.Should().Be(2);
        result.Value.Rows.Sum(r => r.UnsettledCreditTotal).Should().Be(total.UnsettledCreditTotal);
    }

    /// <summary>
    /// ★ NEVER ONE BLENDED FIGURE. Wasnie holds no exchange rates, so a EUR total and a USD total are
    /// two answers, and one payee owed in both currencies is two open accounts.
    /// </summary>
    [Fact]
    public async Task Two_currencies_are_two_rows_and_two_totals_never_one_blended_figure()
    {
        var h = Build(nameof(Two_currencies_are_two_rows_and_two_totals_never_one_blended_figure));
        var payee = AddPayee(h, "GONE-MULTI", terminated: true);
        AddUnsettledCredit(h, payee.Id, 100m, Eur, reference: "POL-EUR");
        AddUnsettledCredit(h, payee.Id, 200m, Usd, reference: "POL-USD");

        var result = await RunAsync(h);

        result.Value!.Rows.Should().HaveCount(2);
        result.Value.Totals.Select(t => t.Currency).Should().BeEquivalentTo([Eur, Usd]);
        result.Value.Totals.Should().OnlyContain(t => t.PayeeCount == 1);
    }

    // ══ Scoping ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// The queue takes no payee id — it IS the list of who to look at — so it can only be protected by
    /// FILTERING. A caller restricted to their own payee must not be handed a departed colleague's
    /// unpaid commission, which is money and a salary signal at once.
    /// </summary>
    [Fact]
    public async Task A_restricted_caller_sees_only_the_payees_they_may_read()
    {
        var probe = Build("visibility-probe");
        var mine = AddPayee(probe, "MINE", terminated: true);
        var theirs = AddPayee(probe, "THEIRS", terminated: true);
        AddUnsettledCredit(probe, mine.Id, 100m, reference: "POL-MINE");
        AddUnsettledCredit(probe, theirs.Id, 999m, reference: "POL-THEIRS");

        // Same in-memory store, a caller who may only read one of the two.
        var guard = Substitute.For<IPayeeAccessGuard>();
        guard.GetVisibilityAsync(Arg.Any<CancellationToken>()).Returns(PayeeVisibility.Of(mine.Id));
        var restricted = new ListTerminatedPayeesWithBalanceHandler(
            probe.Db, Substitute.For<IAuthorizationService>(), guard);

        var result = await restricted.Handle(
            new ListTerminatedPayeesWithBalanceQuery(), CancellationToken.None);

        result.Value!.Rows.Should().ContainSingle().Which.PayeeId.Should().Be(mine.Id);
        result.Value.Totals.Single().UnsettledCreditTotal.Should().Be(100m,
            "the total must be of the visible rows, not of the tenant");
    }

    /// <summary>
    /// Another tenant's departed payees are not in the population at all — the query filter removes
    /// them before visibility is even consulted.
    /// </summary>
    [Fact]
    public async Task The_queue_never_reaches_another_tenants_unpaid_commission()
    {
        var h = Build(nameof(The_queue_never_reaches_another_tenants_unpaid_commission));
        var mine = AddPayee(h, "T-A-GONE", terminated: true);
        AddUnsettledCredit(h, mine.Id, 100m, reference: "POL-A");

        // A payee of a DIFFERENT tenant, written straight into the same store.
        var otherTenant = Guid.NewGuid();
        var stranger = Payee.Create(otherTenant, "Stranger", "T-B-GONE", "b@other.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        stranger.MarkAsTerminated(new DateOnly(2026, 6, 30), "hr@other.com", Now);
        h.Db.Payees.Add(stranger);
        h.Db.SaveChanges();

        var result = await RunAsync(h);

        result.Value!.Rows.Should().ContainSingle().Which.PayeeId.Should().Be(mine.Id);
    }
}
