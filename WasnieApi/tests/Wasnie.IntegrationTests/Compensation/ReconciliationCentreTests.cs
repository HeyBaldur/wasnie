using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Compensation.Commands.Reconciliation;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;
using Wasnie.IntegrationTests.TestDoubles;
using Wasnie.Application.Compensation.Handlers.Dashboard;
using Wasnie.Application.Compensation.Queries.Dashboard;
using Wasnie.Application.Compensation.Handlers.Sidebar;
using Wasnie.Application.Compensation.Queries.Sidebar;
using Wasnie.Domain.Authorization;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Reconciliation;
using Wasnie.Application.Compensation.Queries.Reconciliation;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Integrations.Crm;

namespace Wasnie.IntegrationTests.Compensation;

/// <summary>
/// KAN-28 tanda B — the Reconciliation Centre's money rules, against a real SQL Server.
///
/// ★★ THESE RUN THE ACTUAL COMPOSED QUERY. The screen's central promise is "the card matches the
/// table", and that can only be proved where the query really executes: EF's translation of a
/// Concat-ed union with Distinct and GroupBy is exactly the kind of thing that behaves differently
/// in memory than in SQL, and an in-memory provider would have proved nothing (§A2).
/// </summary>
[Collection(CreditAllocationServiceCollection.Name)]
public sealed class ReconciliationCentreTests(CreditAllocationServiceFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TxDate = new(2026, 3, 15);
    private const string EUR = "EUR";

    private static Payee MakePayee(Guid tenantId, Guid payeeId, string code) =>
        Payee.Create(tenantId, "Queue Payee", code, $"{code}@test.com",
            new DateOnly(2020, 1, 1), "test-user", payeeId, Now);

    private static Plan MakePlan(Guid tenantId, Guid planId, string name = "Queue Plan")
    {
        var plan = Plan.Create(tenantId, name, "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            EUR, "test-user", planId, Now, Guid.NewGuid());
        plan.AddRule("Rule", 1,
            new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
            RateTable.Flat(0.1m));
        return plan;
    }

    private static Credit RefusedCredit(
        Guid tenantId, Guid txId, Guid payeeId, Guid planId, Guid ruleId,
        decimal baseAmount, string refusal)
    {
        var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Rule",
            RateTable.Flat(0.1m), Trigger.Always(), Now,
            measurement: new Measurement { Type = MeasurementType.Revenue });

        return Credit.Allocate(
            tenantId, txId, payeeId, planId, ruleId, snapshot,
            originalAmount: Money.Of(baseAmount, EUR),
            creditedAmount: Money.Of(0m, EUR),
            splitPercentage: Percentage.FromPercent(100),
            role: CreditRole.Primary,
            allocatedBy: "test",
            id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid(),
            calculationTrace: $$"""{"_schema":1,"creditGenerated":true,"steps":[{"component":"Rate","outcome":"Skipped","rateRefusal":"{{refusal}}"}]}""",
            rateRefusal: refusal);
    }

    private static CompensationTransaction Tx(
        Guid tenantId, string reference, Guid? payeeId, decimal amount, string currency = EUR) =>
        CompensationTransaction.Ingest(
            tenantId, reference, payeeId, Money.Of(amount, currency), TxDate,
            TransactionSource.Manual, "user", Guid.NewGuid(), Now, Guid.NewGuid());

    /// <summary>
    /// A transaction whose commission has already been calculated and paid.
    ///
    /// ★ THE FIRST VERSION OF THESE TESTS LEFT IT Pending, AND THE QUERY WAS RIGHT TO OBJECT: a
    /// Pending row with a payee and no assignment really does also carry NoActiveAssignment, so the
    /// entry legitimately had three reasons. A deal that was lost or drifted has by definition
    /// already been through the engine. The fixture was describing a state production cannot produce.
    /// </summary>
    private static CompensationTransaction PaidTx(
        Guid tenantId, string reference, Guid payeeId, decimal amount)
    {
        var tx = Tx(tenantId, reference, payeeId, amount);
        tx.MarkCalculated(1, Money.Of(amount * 0.05m, EUR), "test", Now, Guid.NewGuid());
        tx.MarkPaid("test", Now, Guid.NewGuid());
        return tx;
    }

    private async Task<ReconciliationPageDto> RunAsync(
        Guid tenantId, ReconciliationFilter? filter = null)
    {
        await using var db = fixture.CreateDbForTenant(tenantId);
        var handler = new GetReconciliationHandler(db, new AlwaysAllowAuthorization());
        var result = await handler.Handle(
            new GetReconciliationQuery(filter ?? new ReconciliationFilter(PageSize: 100)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }

    // ══ A refused credit reaches the queue with its reason ════════════════════════════════════

    [Fact]
    public async Task A_credit_refused_by_the_engine_appears_with_its_reason_and_the_sale_behind_it()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlan(tenantId, planId);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId, "EMP-REF"));

            var tx = Tx(tenantId, "REF-REFUSED", payeeId, 50_000m);
            db.CompensationTransactions.Add(tx);

            db.Credits.Add(RefusedCredit(tenantId, tx.Id, payeeId, planId,
                plan.Rules.First().Id, 50_000m, ReconciliationReason.NoQuotaInEffect));
            await db.SaveChangesAsync();
        }

        var page = await RunAsync(tenantId);

        var row = page.Items.Should().ContainSingle(i => i.Kind == ReconciliationEntryKind.Credit).Subject;
        row.Reasons.Should().ContainSingle().Which.Should().Be(ReconciliationReason.NoQuotaInEffect);
        row.PayeeName.Should().Be("Queue Payee");
        row.PlanName.Should().Be("Queue Plan");
        row.ReferenceNumber.Should().Be("REF-REFUSED");

        // ★ The SALE, not a commission. The commission is the number the refusal says nobody knows.
        row.Amount.Should().Be(50_000m);
        row.MoneyKind.Should().Be(ReconciliationMoneyKind.AffectedBase);
    }

    // ══ A transaction from the shared spec reaches the queue ══════════════════════════════════

    [Fact]
    public async Task A_pending_transaction_with_no_payee_appears_from_the_shared_spec()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationTransactions.Add(Tx(tenantId, "REF-NOPAYEE", null, 1_500m));
            await db.SaveChangesAsync();
        }

        var page = await RunAsync(tenantId);

        var row = page.Items.Should().ContainSingle().Subject;
        row.Kind.Should().Be(ReconciliationEntryKind.Transaction);
        row.Reasons.Should().Equal(ReconciliationReason.NoPayee);
        row.Amount.Should().Be(1_500m);
    }

    /// <summary>
    /// ★★ THE HARD REQUIREMENT OF THE TICKET: the dashboard card and this screen must never disagree
    /// about the same money. They agree because they run the SAME queryable, and this proves it on
    /// real data rather than by inspection.
    /// </summary>
    [Fact]
    public async Task The_queue_and_the_dashboard_spec_return_the_same_count_for_the_same_money()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationTransactions.Add(Tx(tenantId, "REF-A", null, 100m));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-B", null, 200m));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-C", null, 300m));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var dashboardCount = await UnprocessablePendingSpec.NoPayee(db).CountAsync();

            var handler = new GetReconciliationHandler(db, new AlwaysAllowAuthorization());
            var page = (await handler.Handle(new GetReconciliationQuery(
                new ReconciliationFilter(Reason: ReconciliationReason.NoPayee, PageSize: 100)),
                CancellationToken.None)).Value!;

            dashboardCount.Should().Be(3);
            page.TotalCount.Should().Be(dashboardCount);
            page.Summary.ByReason
                .Single(r => r.Reason == ReconciliationReason.NoPayee)
                .Count.Should().Be(dashboardCount);
        }
    }

    // ══ Aggregates ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ THE CARD MUST EQUAL THE TABLE. Not approximately, and not "for the current page": the
    /// summary is computed over the whole filtered set by the same query that produced the rows.
    /// </summary>
    [Fact]
    public async Task The_currency_totals_equal_the_sum_of_the_rows_behind_them()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationTransactions.Add(Tx(tenantId, "REF-E1", null, 1_000m));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-E2", null, 2_500.50m));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-U1", null, 700m, "USD"));
            await db.SaveChangesAsync();
        }

        var page = await RunAsync(tenantId);

        var eur = page.Summary.ByCurrency.Single(c => c.Currency == EUR);
        var usd = page.Summary.ByCurrency.Single(c => c.Currency == "USD");

        eur.AffectedBaseAmount.Should().Be(3_500.50m);
        usd.AffectedBaseAmount.Should().Be(700m);

        // And the same figures reconstructed from the rows themselves.
        page.Items.Where(i => i.Currency == EUR).Sum(i => i.Amount ?? 0m).Should().Be(eur.AffectedBaseAmount);
        page.Items.Where(i => i.Currency == "USD").Sum(i => i.Amount ?? 0m).Should().Be(usd.AffectedBaseAmount);
        page.Summary.TotalRows.Should().Be(3);
    }

    /// <summary>
    /// ★★ CLAWBACK AND UNPAID COMMISSION NEVER MEET. Two figures on the same currency line, and no
    /// field anywhere holds their difference — a net would let one hide the other.
    /// </summary>
    [Fact]
    public async Task Clawback_and_unpaid_commission_are_two_separate_figures_never_a_net()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId, "EMP-CLAW"));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-UNPAID", null, 1_000m));

            var paidTx = PaidTx(tenantId, "REF-LOST", payeeId, 9_999m);
            db.CompensationTransactions.Add(paidTx);
            db.DealLostAlerts.Add(DealLostAlert.Create(
                Guid.NewGuid(), tenantId, "hubspot", "deal-1", paidTx.Id, "REF-LOST",
                CompensationTransactionStatus.Paid, 400m, EUR, Now, "test"));
            await db.SaveChangesAsync();
        }

        var page = await RunAsync(tenantId);
        var eur = page.Summary.ByCurrency.Single(c => c.Currency == EUR);

        eur.AffectedBaseAmount.Should().Be(1_000m);
        eur.ClawbackAmount.Should().Be(400m);

        // 600 would be the net. It must appear nowhere.
        eur.AffectedBaseAmount.Should().NotBe(600m);
    }

    // ══ Two reasons, one row ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ ONE ENTRY, BOTH REASONS, COUNTED ONCE. A transaction whose deal both drifted and was lost
    /// is one problem, not two: it appears once carrying both codes, contributes its money once, and
    /// still shows up under each reason's count.
    /// </summary>
    [Fact]
    public async Task An_entry_with_two_reasons_appears_once_with_both_and_is_not_counted_twice()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId, "EMP-BOTH"));
            var tx = PaidTx(tenantId, "REF-BOTH", payeeId, 5_000m);
            db.CompensationTransactions.Add(tx);

            db.DealLostAlerts.Add(DealLostAlert.Create(
                Guid.NewGuid(), tenantId, "hubspot", "deal-2", tx.Id, "REF-BOTH",
                CompensationTransactionStatus.Paid, 250m, EUR, Now, "test"));

            db.CrmDriftAlerts.Add(CrmDriftAlert.Create(
                Guid.NewGuid(), tenantId, "hubspot", "deal-2", tx.Id, "REF-BOTH",
                CompensationTransactionStatus.Paid,
                amountChanged: true, oldAmount: 5_000m, oldCurrency: EUR,
                newAmount: 4_000m, newCurrency: EUR,
                dateChanged: false, oldCloseDate: TxDate, newCloseDate: TxDate,
                detectedAt: Now, detectedBy: "test"));

            await db.SaveChangesAsync();
        }

        var page = await RunAsync(tenantId);

        var row = page.Items.Should().ContainSingle().Subject;
        row.Reasons.Should().BeEquivalentTo([ReconciliationReason.CrmDrift, ReconciliationReason.DealLost]);

        page.Summary.TotalRows.Should().Be(1, "two reasons are still one entry");
        page.TotalCount.Should().Be(1);

        // The money is counted ONCE, not once per reason.
        page.Summary.ByCurrency.Single(c => c.Currency == EUR).ClawbackAmount.Should().Be(250m);

        // And it appears under each reason's count.
        page.Summary.ByReason.Single(r => r.Reason == ReconciliationReason.DealLost).Count.Should().Be(1);
        page.Summary.ByReason.Single(r => r.Reason == ReconciliationReason.CrmDrift).Count.Should().Be(1);
    }

    /// <summary>
    /// Filtering by ONE of a two-reason entry's reasons still returns the entry with BOTH. Showing
    /// only the matched reason would tell the reader the entry failed for one thing when it failed
    /// for two.
    /// </summary>
    [Fact]
    public async Task Filtering_by_one_reason_returns_the_entry_with_all_of_its_reasons()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId, "EMP-FILTER"));
            var tx = PaidTx(tenantId, "REF-FILTER", payeeId, 5_000m);
            db.CompensationTransactions.Add(tx);

            db.DealLostAlerts.Add(DealLostAlert.Create(
                Guid.NewGuid(), tenantId, "hubspot", "deal-3", tx.Id, "REF-FILTER",
                CompensationTransactionStatus.Paid, 250m, EUR, Now, "test"));
            db.CrmDriftAlerts.Add(CrmDriftAlert.Create(
                Guid.NewGuid(), tenantId, "hubspot", "deal-3", tx.Id, "REF-FILTER",
                CompensationTransactionStatus.Paid,
                amountChanged: true, oldAmount: 5_000m, oldCurrency: EUR,
                newAmount: 4_000m, newCurrency: EUR,
                dateChanged: false, oldCloseDate: TxDate, newCloseDate: TxDate,
                detectedAt: Now, detectedBy: "test"));
            await db.SaveChangesAsync();
        }

        var page = await RunAsync(tenantId,
            new ReconciliationFilter(Reason: ReconciliationReason.DealLost, PageSize: 100));

        var row = page.Items.Should().ContainSingle().Subject;
        row.Reasons.Should().BeEquivalentTo([ReconciliationReason.CrmDrift, ReconciliationReason.DealLost]);
    }

    // ══ A plan is a cause, not a sum ══════════════════════════════════════════════════════════

    [Fact]
    public async Task A_plan_with_no_live_rules_is_listed_but_adds_nothing_to_any_currency_total()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlan(tenantId, planId, "Dead Plan");
            plan.Activate("test", Now, Guid.NewGuid());
            plan.StopRule(plan.Rules.First().Id, "test", "no longer paying", Now);
            db.CompensationPlans.Add(plan);
            await db.SaveChangesAsync();
        }

        var page = await RunAsync(tenantId);

        var row = page.Items.Should().ContainSingle().Subject;
        row.Kind.Should().Be(ReconciliationEntryKind.Plan);
        row.Reasons.Should().Equal(ReconciliationReason.PlanHasNoActiveRules);
        row.Amount.Should().BeNull("a plan is a cause, not a sum");
        row.MoneyKind.Should().Be(ReconciliationMoneyKind.None);

        page.Summary.ByCurrency.Should().BeEmpty();
        page.Summary.TotalRows.Should().Be(1);
    }

    // ══ The two expressions of ambiguity must agree ═══════════════════════════════════════════

    /// <summary>
    /// ★★ THE TEST <c>AmbiguousAttributionSpec.Queryable</c> NAMES IN ITS OWN DOC COMMENT. That
    /// method is a SECOND expression of a rule the engine already owns in memory
    /// (<c>PlanAssignmentResolver.Candidates</c>), and this codebase has been bitten by exactly that
    /// shape before. It exists only because a screen that states totals must compute them in SQL.
    ///
    /// So both are run over the SAME rows and required to pick the same transactions. If this goes
    /// red the QUERYABLE is wrong — the in-memory one is the engine's, and the engine is the
    /// authority.
    ///
    /// The fixture deliberately includes every near-miss: one eligible plan (not ambiguous), two in
    /// the wrong currency (not ambiguous), two where one is archived (not ambiguous), and two genuine
    /// candidates (ambiguous).
    /// </summary>
    [Fact]
    public async Task The_ambiguity_queryable_picks_exactly_what_the_engines_in_memory_rule_picks()
    {
        var tenantId = Guid.NewGuid();

        var ambiguousPayee = Guid.NewGuid();
        var singlePlanPayee = Guid.NewGuid();
        var wrongCurrencyPayee = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, ambiguousPayee, "EMP-AMB"));
            db.Payees.Add(MakePayee(tenantId, singlePlanPayee, "EMP-ONE"));
            db.Payees.Add(MakePayee(tenantId, wrongCurrencyPayee, "EMP-CCY"));

            var planA = MakePlan(tenantId, Guid.NewGuid(), "Plan A");
            var planB = MakePlan(tenantId, Guid.NewGuid(), "Plan B");
            var planUsd = Plan.Create(tenantId, "Plan USD", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                "USD", "test-user", Guid.NewGuid(), Now, Guid.NewGuid());
            planUsd.AddRule("Rule", 1,
                new Measurement { Type = MeasurementType.Revenue },
                RateTable.Flat(0.1m));

            db.CompensationPlans.AddRange(planA, planB, planUsd);

            Assign(db, tenantId, planA.Id, ambiguousPayee);
            Assign(db, tenantId, planB.Id, ambiguousPayee);
            Assign(db, tenantId, planA.Id, singlePlanPayee);
            Assign(db, tenantId, planUsd.Id, wrongCurrencyPayee);
            Assign(db, tenantId, planB.Id, wrongCurrencyPayee);

            db.CompensationTransactions.Add(Tx(tenantId, "REF-AMB", ambiguousPayee, 1_000m));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-ONE", singlePlanPayee, 1_000m));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-CCY", wrongCurrencyPayee, 1_000m, "USD"));

            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var fromSql = await AmbiguousAttributionSpec.Queryable(db)
                .Select(t => t.Id)
                .ToListAsync();

            // The engine's own path, over the same rows.
            var transactions = await db.CompensationTransactions.ToListAsync();
            var assignments = await db.PlanAssignments.ToListAsync();
            var planCurrency = await db.CompensationPlans
                .ToDictionaryAsync(p => p.Id, p => p.Currency);
            var archived = (await db.CompensationPlans
                .Where(p => p.Status == PlanStatus.Archived)
                .Select(p => p.Id)
                .ToListAsync())
                .ToHashSet();

            var fromEngine = transactions
                .Where(t => t.PayeeId.HasValue && AmbiguousAttributionSpec.IsAmbiguous(
                    t,
                    assignments.Where(a => a.PayeeId == t.PayeeId),
                    planCurrency,
                    archived))
                .Select(t => t.Id)
                .ToList();

            fromSql.Should().BeEquivalentTo(fromEngine);
            fromEngine.Should().HaveCount(1, "only the payee on two same-currency plans is ambiguous");
        }

        static void Assign(
            Wasnie.Infrastructure.Persistence.ApplicationDbContext db,
            Guid tenantId, Guid planId, Guid payeeId)
        {
            db.PlanAssignments.Add(PlanAssignment.Create(
                tenantId, planId, payeeId,
                PayeeReference.Snapshot(payeeId, "Queue Payee", "EMP"),
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                "test-user", Guid.NewGuid(), Now, Guid.NewGuid()));
        }
    }


    // ══ KAN-50 — a sale that lacks nothing and still carries no credit ═════════════════════════

    /// <summary>
    /// ★★ THE STATE THAT HAD NO NAME. Payee, Active assignment covering the date, plan in the
    /// transaction's currency, one candidate so nothing to choose — every test the other reasons
    /// apply, this row passes, and it has no credit. Before KAN-50 the queue returned nothing for
    /// it: <c>UnprocessablePendingSpec</c> calls it processable and therefore not its problem, and
    /// <c>AmbiguousAttributionSpec</c> sees a single candidate. The money was real and no screen
    /// could show it.
    ///
    /// ★ THIS IS THE SHAPE OF THE TWO ROWS THAT PRODUCED THE TICKET (SCC-20260515-0002 / -0006),
    /// reproduced rather than described: their assignment was created after they were ingested, and
    /// no processing run has covered them since.
    /// </summary>
    [Fact]
    public async Task A_sale_that_lacks_nothing_and_carries_no_credit_appears_in_the_queue()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId, "EMP-UNPAID"));
            var plan = MakePlan(tenantId, Guid.NewGuid(), "Payable Plan");
            db.CompensationPlans.Add(plan);
            AssignFully(db, tenantId, plan.Id, payeeId);
            db.CompensationTransactions.Add(Tx(tenantId, "REF-UNPAID", payeeId, 234.50m));
            await db.SaveChangesAsync();
        }

        var page = await RunAsync(tenantId);

        var row = page.Items.Should().ContainSingle().Subject;
        row.Kind.Should().Be(ReconciliationEntryKind.Transaction);
        row.Reasons.Should().Equal(ReconciliationReason.ProcessableWithoutCredit);
        // The SALE, like every other Pending reason: the commission is the number nobody knows.
        row.Amount.Should().Be(234.50m);
        page.Summary.ByReason
            .Single(r => r.Reason == ReconciliationReason.ProcessableWithoutCredit)
            .Count.Should().Be(1);
    }

    /// <summary>
    /// ★★ THE FAIL-SAFE. A transaction the engine paid normally must not start reading as an
    /// exception — a queue that lists healthy money is a queue nobody reads.
    /// </summary>
    [Fact]
    public async Task A_sale_that_was_paid_normally_never_reaches_the_queue()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId, "EMP-PAID"));
            var plan = MakePlan(tenantId, Guid.NewGuid(), "Paying Plan");
            db.CompensationPlans.Add(plan);
            AssignFully(db, tenantId, plan.Id, payeeId);

            var tx = Tx(tenantId, "REF-PAID", payeeId, 1_000m);
            tx.MarkCalculated(1, Money.Of(100m, EUR), "test", Now, Guid.NewGuid());
            db.CompensationTransactions.Add(tx);
            db.Credits.Add(PaidCredit(tenantId, tx.Id, payeeId, plan.Id, plan.Rules.First().Id, 1_000m));
            await db.SaveChangesAsync();
        }

        var page = await RunAsync(tenantId);

        page.Items.Should().BeEmpty();
    }

    /// <summary>
    /// ★★ THE REGRESSION THE TICKET ASKS FOR, stated as exclusivity rather than as four separate
    /// assertions: every transaction that already had a reason keeps exactly that reason, and the
    /// new one claims none of them. A bucket that overlapped would make one unpaid sale read as two
    /// problems and inflate the count the screen exists to state precisely.
    /// </summary>
    [Fact]
    public async Task The_new_reason_never_overlaps_the_reasons_that_already_existed()
    {
        var tenantId = Guid.NewGuid();
        var noAssignmentPayee = Guid.NewGuid();
        var wrongCurrencyPayee = Guid.NewGuid();
        var ambiguousPayee = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, noAssignmentPayee, "EMP-NOASG"));
            db.Payees.Add(MakePayee(tenantId, wrongCurrencyPayee, "EMP-CCY"));
            db.Payees.Add(MakePayee(tenantId, ambiguousPayee, "EMP-AMB"));

            var planA = MakePlan(tenantId, Guid.NewGuid(), "Plan A");
            var planB = MakePlan(tenantId, Guid.NewGuid(), "Plan B");
            db.CompensationPlans.AddRange(planA, planB);

            AssignFully(db, tenantId, planA.Id, wrongCurrencyPayee);
            AssignFully(db, tenantId, planA.Id, ambiguousPayee);
            AssignFully(db, tenantId, planB.Id, ambiguousPayee);

            db.CompensationTransactions.Add(Tx(tenantId, "REF-NOPAYEE", null, 100m));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-NOASG", noAssignmentPayee, 200m));
            // EUR plan, USD sale.
            db.CompensationTransactions.Add(Tx(tenantId, "REF-CCY", wrongCurrencyPayee, 300m, "USD"));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-AMB", ambiguousPayee, 400m));
            await db.SaveChangesAsync();
        }

        var page = await RunAsync(tenantId);

        page.Items.Should().HaveCount(4);
        page.Items.SelectMany(i => i.Reasons)
            .Should().NotContain(ReconciliationReason.ProcessableWithoutCredit);

        page.Items.Single(i => i.Amount == 100m).Reasons.Should().Equal(ReconciliationReason.NoPayee);
        page.Items.Single(i => i.Amount == 200m).Reasons.Should().Equal(ReconciliationReason.NoActiveAssignment);
        page.Items.Single(i => i.Amount == 300m).Reasons.Should().Equal(ReconciliationReason.CurrencyMismatch);
        page.Items.Single(i => i.Amount == 400m).Reasons.Should().Equal(ReconciliationReason.AmbiguousAttribution);
    }

    /// <summary>
    /// ★★ THE SECOND EXPRESSION, PINNED. The spec is SQL because the queue pages and totals in SQL,
    /// while eligibility itself lives in <c>PlanAssignmentResolver.Candidates</c> in memory because
    /// the engine calls it per transaction. Two expressions of one rule is the risk this codebase
    /// has been bitten by, so they are run over the same rows and required to agree. If this goes
    /// red the SQL is wrong: the engine is the authority.
    /// </summary>
    [Fact]
    public async Task The_queryable_selects_exactly_the_sales_the_engine_calls_payable_and_unpaid()
    {
        var tenantId = Guid.NewGuid();
        var payablePayee = Guid.NewGuid();
        var archivedPlanPayee = Guid.NewGuid();
        var deactivatedPayee = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payablePayee, "EMP-PAY"));
            db.Payees.Add(MakePayee(tenantId, archivedPlanPayee, "EMP-ARC"));
            db.Payees.Add(MakePayee(tenantId, deactivatedPayee, "EMP-DEA"));

            var live = MakePlan(tenantId, Guid.NewGuid(), "Live Plan");
            var archived = MakePlan(tenantId, Guid.NewGuid(), "Archived Plan");
            archived.Activate("test-user", Now, Guid.NewGuid());
            archived.Archive("test-user", Now, Guid.NewGuid());
            var forDeactivated = MakePlan(tenantId, Guid.NewGuid(), "Deactivated Plan");
            db.CompensationPlans.AddRange(live, archived, forDeactivated);

            AssignFully(db, tenantId, live.Id, payablePayee);
            AssignFully(db, tenantId, archived.Id, archivedPlanPayee);

            var dead = PlanAssignment.Create(
                tenantId, forDeactivated.Id, deactivatedPayee,
                PayeeReference.Snapshot(deactivatedPayee, "Queue Payee", "EMP"),
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                "test-user", Guid.NewGuid(), Now, Guid.NewGuid());
            dead.Deactivate("test-user", Now, Guid.NewGuid());
            db.PlanAssignments.Add(dead);

            db.CompensationTransactions.Add(Tx(tenantId, "REF-PAY", payablePayee, 1_000m));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-ARC", archivedPlanPayee, 1_000m));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-DEA", deactivatedPayee, 1_000m));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var fromSql = await ProcessableWithoutCreditSpec.Queryable(db)
                .Select(t => t.Id)
                .ToListAsync();

            // The engine's own path, over the same rows.
            var transactions = await db.CompensationTransactions.ToListAsync();
            var assignments = await db.PlanAssignments.ToListAsync();
            var creditedTxIds = (await db.Credits
                .Where(c => c.SupersededAt == null)
                .Select(c => c.TransactionId)
                .ToListAsync())
                .ToHashSet();
            var planCurrency = await db.CompensationPlans
                .ToDictionaryAsync(p => p.Id, p => p.Currency);
            var archivedPlanIds = (await db.CompensationPlans
                .Where(p => p.Status == PlanStatus.Archived)
                .Select(p => p.Id)
                .ToListAsync())
                .ToHashSet();

            var fromEngine = transactions
                .Where(t => t.PayeeId.HasValue && !creditedTxIds.Contains(t.Id))
                .Where(t =>
                {
                    var candidates = Wasnie.Application.Compensation.Calculation.PlanAssignmentResolver.Candidates(
                        assignments.Where(a => a.PayeeId == t.PayeeId),
                        t.TransactionDate, t.Amount.Currency, planCurrency, archivedPlanIds);

                    // 1+ candidate, minus the ambiguous case the other spec owns.
                    return candidates.Count >= 1
                        && (t.SelectedPlanAssignmentId != null || candidates.Count == 1);
                })
                .Select(t => t.Id)
                .ToList();

            fromSql.Should().BeEquivalentTo(fromEngine);
            fromEngine.Should().HaveCount(1,
                "an archived plan and a deactivated assignment are not candidates the engine would honour");
        }
    }

    /// <summary>An Active assignment covering the whole of 2026, the shape every KAN-50 test needs.</summary>
    private static void AssignFully(
        Wasnie.Infrastructure.Persistence.ApplicationDbContext db,
        Guid tenantId, Guid planId, Guid payeeId) =>
        db.PlanAssignments.Add(PlanAssignment.Create(
            tenantId, planId, payeeId,
            PayeeReference.Snapshot(payeeId, "Queue Payee", "EMP"),
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "test-user", Guid.NewGuid(), Now, Guid.NewGuid()));

    /// <summary>A normal, non-refused credit — the money a healthy transaction carries.</summary>
    private static Credit PaidCredit(
        Guid tenantId, Guid txId, Guid payeeId, Guid planId, Guid ruleId, decimal baseAmount)
    {
        var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Rule",
            RateTable.Flat(0.1m), Trigger.Always(), Now,
            measurement: new Measurement { Type = MeasurementType.Revenue });

        return Credit.Allocate(
            tenantId, txId, payeeId, planId, ruleId, snapshot,
            originalAmount: Money.Of(baseAmount, EUR),
            creditedAmount: Money.Of(baseAmount * 0.1m, EUR),
            splitPercentage: Percentage.FromPercent(100),
            role: CreditRole.Primary,
            allocatedBy: "test",
            id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid(),
            calculationTrace: "{\"_schema\":1,\"creditGenerated\":true,\"steps\":[]}",
            rateRefusal: null);
    }

    // ══ KAN-51: closing a row by human decision ══════════════════════════════════════════════

    private async Task<Result<CloseReconciliationRowResult>> CloseAsync(
        Guid tenantId, ReconciliationEntryKind kind, Guid entityId, string note)
    {
        await using var db = fixture.CreateDbForTenant(tenantId);
        var handler = new CloseReconciliationRowHandler(
            db,
            new CreditAllocationServiceFixture.FixedTenantContext(tenantId),
            new FixedCurrentUser(),
            new FakeClock(),
            new FakeGuidGenerator(),
            new AlwaysAllowAuthorization());

        return await handler.Handle(
            new CloseReconciliationRowCommand(kind, entityId, note), CancellationToken.None);
    }

    /// <summary>
    /// The ticket's second and third acceptance criteria at once: the closure exists with who, when
    /// and why; the underlying record is untouched; and the row is gone from BOTH the table and the
    /// totals.
    ///
    /// ★ THE TOTALS ARE ASSERTED, NOT ONLY THE ROWS. A closure applied after the aggregates were
    /// computed would leave a card claiming money the table no longer lists — the exact failure this
    /// screen exists to prevent. It is why the exclusion lives in Filtered(), upstream of both.
    /// </summary>
    [Fact]
    public async Task A_closed_row_leaves_the_queue_and_the_totals_and_the_transaction_is_untouched()
    {
        var tenantId = Guid.NewGuid();
        Guid txId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = Tx(tenantId, "REF-CLOSE-ME", null, 4_000m);
            txId = tx.Id;
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();
        }

        var before = await RunAsync(tenantId);
        before.Items.Should().ContainSingle();
        before.Summary.ByCurrency.Single().AffectedBaseAmount.Should().Be(4_000m);

        var result = await CloseAsync(
            tenantId, ReconciliationEntryKind.Transaction, txId,
            "Legacy import with no payee on record; nothing left to attribute.");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.ClosedReasons.Should().Equal(ReconciliationReason.NoPayee);

        var after = await RunAsync(tenantId);
        after.Items.Should().BeEmpty("a closed row leaves the table");
        after.Summary.TotalRows.Should().Be(0);
        after.Summary.ByCurrency.Should().BeEmpty("and it leaves the money cards with it");
        after.Summary.ByReason.Should().BeEmpty();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var closure = await db.ReconciliationClosures.SingleAsync();
            closure.EntityId.Should().Be(txId);
            closure.Reason.Should().Be(ReconciliationReason.NoPayee);
            closure.Note.Should().Be("Legacy import with no payee on record; nothing left to attribute.");
            closure.ClosedByUserId.Should().Be("closer-user");
            closure.ClosedAt.Should().NotBe(default);

            // ★ THE ORIGINAL RECORD IS UNTOUCHED. Not "we remembered not to set a flag" — there is
            // no flag, and the handler never loads the transaction in order to modify it.
            var tx = await db.CompensationTransactions.SingleAsync(t => t.Id == txId);
            tx.PayeeId.Should().BeNull();
            tx.Status.Should().Be(CompensationTransactionStatus.Pending);
            tx.CancelledAt.Should().BeNull();
        }
    }

    /// <summary>
    /// ★★ THE PRODUCT DECISION, AS A TEST. A closure is immutable over the fact it reviewed; a NEWER
    /// detection of the same anomaly is a new fact and comes back. Nothing revives and nothing is
    /// edited — the closure row is exactly as it was, and the newer alert simply falls outside it.
    ///
    /// The alternative — keying the closure on the entity alone — would hide this second detection
    /// for ever, which is money disappearing without anybody deciding it should (§B1).
    /// </summary>
    [Fact]
    public async Task A_reobserved_alert_stays_closed_but_a_new_alert_returns()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        Guid txId;
        Guid alertId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId, "EMP-LATER"));
            var tx = PaidTx(tenantId, "REF-LOST-TWICE", payeeId, 9_000m);
            txId = tx.Id;
            db.CompensationTransactions.Add(tx);

            alertId = Guid.NewGuid();
            db.DealLostAlerts.Add(DealLostAlert.Create(
                alertId, tenantId, "HubSpot", "deal-1", tx.Id, "REF-LOST-TWICE",
                CompensationTransactionStatus.Paid, 450m, EUR, Now, "sync"));
            await db.SaveChangesAsync();
        }

        (await RunAsync(tenantId)).Items.Should().ContainSingle();

        var closed = await CloseAsync(
            tenantId, ReconciliationEntryKind.Transaction, txId,
            "Commission was already paid and the clawback is applied; nothing to repair.");
        closed.IsSuccess.Should().BeTrue(closed.Error);

        (await RunAsync(tenantId)).Items.Should().BeEmpty();

        // ★★ THE SYNC RE-OBSERVES THE SAME ALERT. Refresh() moves DetectedAt forward without anything
        // having changed — the hourly HubSpot job does this to every open alert. This is NOT a new
        // fact, and the row must stay closed.
        //
        // This test used to assert the opposite, and that is how the defect shipped: the first design
        // compared timestamps, so every sync expired every closure and a row the user had closed came
        // back within the hour. Four real closures were void before anybody noticed.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var alert = await db.DealLostAlerts.SingleAsync(a => a.Id == alertId);
            alert.Refresh(CompensationTransactionStatus.Paid, 450m, EUR, Now.AddDays(30), "sync");
            await db.SaveChangesAsync();
        }

        (await RunAsync(tenantId)).Items.Should().BeEmpty(
            "re-observing an open alert is the same fact seen again, not a new one");

        // ★ A GENUINELY NEW LOSS IS A NEW ALERT. Resolve the old one and raise another: different id,
        // different fact, and it surfaces — which is the property the timestamp was reaching for.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var alert = await db.DealLostAlerts.SingleAsync(a => a.Id == alertId);
            alert.Resolve("test", Now.AddDays(40));

            db.DealLostAlerts.Add(DealLostAlert.Create(
                Guid.NewGuid(), tenantId, "HubSpot", "deal-1", txId, "REF-LOST-TWICE",
                CompensationTransactionStatus.Paid, 450m, EUR, Now.AddDays(45), "sync"));
            await db.SaveChangesAsync();
        }

        var after = await RunAsync(tenantId);
        after.Items.Should().ContainSingle("a NEW alert is a new fact");
        after.Items[0].Reasons.Should().Equal(ReconciliationReason.DealLost);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var closure = await db.ReconciliationClosures.SingleAsync();
            closure.FactOccurredAt.Should().Be(Now,
                "the closure records the fact it reviewed and is never edited");
            closure.FactKey.Should().Be(alertId, "and it remembers WHICH alert it closed");
        }
    }

    /// <summary>
    /// ★★ CLOSING ONE REASON MUST NOT SWALLOW ANOTHER. A transaction that is both drifted and lost
    /// is two judgements; closing it while only the drift was known must leave the lost deal visible
    /// when it is detected. This is the case that puts the reason in the closure key.
    /// </summary>
    [Fact]
    public async Task Closing_a_row_does_not_hide_a_different_anomaly_found_on_the_same_entity_later()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        Guid txId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId, "EMP-BOTH"));
            var tx = PaidTx(tenantId, "REF-BOTH-CLOSE", payeeId, 12_000m);
            txId = tx.Id;
            db.CompensationTransactions.Add(tx);

            db.CrmDriftAlerts.Add(CrmDriftAlert.Create(
                Guid.NewGuid(), tenantId, "hubspot", "deal-drift", tx.Id, "REF-BOTH-CLOSE",
                CompensationTransactionStatus.Paid,
                amountChanged: true, oldAmount: 12_000m, oldCurrency: EUR,
                newAmount: 11_999.99m, newCurrency: EUR,
                dateChanged: false, oldCloseDate: TxDate, newCloseDate: TxDate,
                detectedAt: Now, detectedBy: "test"));
            await db.SaveChangesAsync();
        }

        var closed = await CloseAsync(
            tenantId, ReconciliationEntryKind.Transaction, txId,
            "Amount changed by a cent; immaterial.");
        closed.IsSuccess.Should().BeTrue(closed.Error);
        closed.Value!.ClosedReasons.Should().Equal(ReconciliationReason.CrmDrift);

        (await RunAsync(tenantId)).Items.Should().BeEmpty();

        // The SAME transaction is later found to be a lost deal — a different reason entirely.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.DealLostAlerts.Add(DealLostAlert.Create(
                Guid.NewGuid(), tenantId, "HubSpot", "deal-2", txId, "REF-BOTH-CLOSE",
                CompensationTransactionStatus.Paid, 600m, EUR, Now.AddDays(5), "sync"));
            await db.SaveChangesAsync();
        }

        var after = await RunAsync(tenantId);
        var row = after.Items.Should().ContainSingle().Subject;
        // The drift stays closed; the lost deal is a separate, unjudged fact and comes through.
        row.Reasons.Should().Equal(ReconciliationReason.DealLost);
    }

    /// <summary>
    /// ★ THE MANDATORY NOTE IS A SERVER INVARIANT, NOT ONLY A DISABLED BUTTON (§D2). This endpoint is
    /// reachable without the modal, and a closure with no stated reason is precisely the row an
    /// auditor would ask about. Whitespace counts as empty: three spaces explain nothing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_closure_without_a_stated_reason_is_refused(string note)
    {
        var tenantId = Guid.NewGuid();
        Guid txId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = Tx(tenantId, "REF-NO-NOTE", null, 700m);
            txId = tx.Id;
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();
        }

        var result = await CloseAsync(tenantId, ReconciliationEntryKind.Transaction, txId, note);

        result.IsSuccess.Should().BeFalse();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            (await db.ReconciliationClosures.CountAsync()).Should().Be(0);
        }

        (await RunAsync(tenantId)).Items.Should().ContainSingle("a refused closure hides nothing");
    }

    /// <summary>
    /// ★★ THE EXCLUSION READS ReconciliationClosures, NEVER AuditLogs — the ticket's fourth
    /// criterion. An AuditLog row naming this action against this resource must move nothing: the
    /// audit log records what people did, and until KAN-34 it recorded actions that never happened.
    /// Evidence deciding what a CFO stops seeing cannot come from there.
    /// </summary>
    [Fact]
    public async Task An_audit_log_entry_alone_hides_nothing()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = Tx(tenantId, "REF-PHANTOM", null, 3_300m);
            db.CompensationTransactions.Add(tx);

            db.AuditLogs.Add(AuditLog.Create(
                tenantId, Now.UtcDateTime, "ghost-user", "ghost@test.com",
                AuditActions.ReconciliationRowClosed, ResourceTypes.Reconciliation,
                tx.Id.ToString()));
            await db.SaveChangesAsync();
        }

        var page = await RunAsync(tenantId);
        page.Items.Should().ContainSingle("only ReconciliationClosures can remove a row");
        page.Summary.ByCurrency.Single().AffectedBaseAmount.Should().Be(3_300m);
    }

    /// <summary>
    /// ★ CLOSING SOMETHING THAT IS NOT OPEN IS REPORTED, NOT SILENTLY ACCEPTED. Two people on the
    /// same screen, or a row fixed between the page loading and the click: answering "done" would
    /// leave the second person believing they recorded a decision that was never written (§B1).
    /// </summary>
    [Fact]
    public async Task Closing_a_row_that_is_no_longer_open_is_refused_rather_than_silently_accepted()
    {
        var tenantId = Guid.NewGuid();
        Guid txId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = Tx(tenantId, "REF-TWICE", null, 500m);
            txId = tx.Id;
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();
        }

        (await CloseAsync(tenantId, ReconciliationEntryKind.Transaction, txId, "Reviewed."))
            .IsSuccess.Should().BeTrue();

        var second = await CloseAsync(
            tenantId, ReconciliationEntryKind.Transaction, txId, "Reviewed again.");

        second.IsSuccess.Should().BeFalse("nothing is open under this key any more");

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            (await db.ReconciliationClosures.CountAsync()).Should().Be(1, "no second row is written");
        }
    }


    // ══ KAN-51 (corrección): el cierre vale para TODA superficie, no sólo para el Centro ══════

    private async Task<DashboardSummaryDto> DashboardAsync(Guid tenantId)
    {
        await using var db = fixture.CreateDbForTenant(tenantId);
        var handler = new GetDashboardSummaryHandler(db, new AlwaysAllowAuthorization(), new FakeClock());
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }

    /// <summary>
    /// ★★ THE BUG THIS TEST EXISTS FOR, FOUND IN RUNTIME AND NOT BY ANY SUITE. The first cut of
    /// KAN-51 put the closure exclusion inside the Reconciliation Centre's query only. The dashboard
    /// reads <c>db.DealLostAlerts</c> directly, so a deal somebody had reviewed and closed vanished
    /// from the Centre and kept alerting on the dashboard — two screens disagreeing about the same
    /// money, which is exactly the drift the Centre was built not to create.
    ///
    /// ★ IT ASSERTS THE DASHBOARD'S OWN OUTPUT, not the query behind it (§A3). What was broken was a
    /// surface nobody had pointed at the rule; only the surface can prove it now does.
    /// </summary>
    [Fact]
    public async Task A_closed_deal_lost_alert_stops_showing_on_the_dashboard_too()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        Guid txId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId, "EMP-DASH"));
            var tx = PaidTx(tenantId, "REF-DASH-LOST", payeeId, 8_000m);
            txId = tx.Id;
            db.CompensationTransactions.Add(tx);

            db.DealLostAlerts.Add(DealLostAlert.Create(
                Guid.NewGuid(), tenantId, "HubSpot", "deal-dash", tx.Id, "REF-DASH-LOST",
                CompensationTransactionStatus.Paid, 400m, EUR, Now, "sync"));
            await db.SaveChangesAsync();
        }

        (await DashboardAsync(tenantId)).ActionBand.DealLostAlerts
            .Should().ContainSingle("the alert is open before anybody reviews it");

        var closed = await CloseAsync(
            tenantId, ReconciliationEntryKind.Transaction, txId,
            "Commission was already paid and the clawback is applied; nothing to repair.");
        closed.IsSuccess.Should().BeTrue(closed.Error);

        (await RunAsync(tenantId)).Items.Should().BeEmpty();
        (await DashboardAsync(tenantId)).ActionBand.DealLostAlerts
            .Should().BeEmpty("a closure is about the anomaly, not about one screen");
    }

    /// <summary>
    /// ★ AND IT STILL COMES BACK ON THE DASHBOARD WHEN THE FACT IS NEWER. The dashboard honours the
    /// same <c>fact &lt;= FactOccurredAt</c> comparison as the Centre, so a re-detection alerts again
    /// rather than staying silently suppressed. Pinning it on this surface too is what stops the two
    /// from drifting apart the next time one of them is edited.
    /// </summary>
    [Fact]
    public async Task A_re_detected_deal_alerts_on_the_dashboard_again()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        Guid txId;
        Guid alertId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId, "EMP-DASH2"));
            var tx = PaidTx(tenantId, "REF-DASH-AGAIN", payeeId, 6_000m);
            txId = tx.Id;
            db.CompensationTransactions.Add(tx);

            alertId = Guid.NewGuid();
            db.DealLostAlerts.Add(DealLostAlert.Create(
                alertId, tenantId, "HubSpot", "deal-again", tx.Id, "REF-DASH-AGAIN",
                CompensationTransactionStatus.Paid, 300m, EUR, Now, "sync"));
            await db.SaveChangesAsync();
        }

        (await CloseAsync(tenantId, ReconciliationEntryKind.Transaction, txId, "Reviewed."))
            .IsSuccess.Should().BeTrue();
        (await DashboardAsync(tenantId)).ActionBand.DealLostAlerts.Should().BeEmpty();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var alert = await db.DealLostAlerts.SingleAsync(a => a.Id == alertId);
            alert.Refresh(CompensationTransactionStatus.Paid, 300m, EUR, Now.AddDays(30), "sync");
            await db.SaveChangesAsync();
        }

        (await DashboardAsync(tenantId)).ActionBand.DealLostAlerts
            .Should().ContainSingle("a newer detection is a new fact on every surface");
    }

    /// <summary>
    /// ★ THE DRIFT PANEL AND THE DEAD-PLAN PANEL FOLLOW THE SAME RULE. They were the deal-lost bug's
    /// identical twins — their own entity, their own dashboard list, no knowledge of closures — and
    /// fixing one of the three would have shipped a known inconsistency in the other two.
    /// </summary>
    [Fact]
    public async Task A_closed_drift_and_a_closed_dead_plan_stop_showing_on_the_dashboard()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        Guid txId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId, "EMP-TWINS"));
            var tx = PaidTx(tenantId, "REF-TWINS", payeeId, 5_000m);
            txId = tx.Id;
            db.CompensationTransactions.Add(tx);

            db.CrmDriftAlerts.Add(CrmDriftAlert.Create(
                Guid.NewGuid(), tenantId, "hubspot", "deal-twins", tx.Id, "REF-TWINS",
                CompensationTransactionStatus.Paid,
                amountChanged: true, oldAmount: 5_000m, oldCurrency: EUR,
                newAmount: 4_900m, newCurrency: EUR,
                dateChanged: false, oldCloseDate: TxDate, newCloseDate: TxDate,
                detectedAt: Now, detectedBy: "test"));

            var plan = MakePlan(tenantId, planId, "Dead Plan");
            plan.Activate("test", Now, Guid.NewGuid());
            plan.StopRule(plan.Rules.First().Id, "test", "no longer paying", Now);
            db.CompensationPlans.Add(plan);

            await db.SaveChangesAsync();
        }

        var before = await DashboardAsync(tenantId);
        before.ActionBand.DriftAlerts.Should().ContainSingle();
        before.ActionBand.PlansWithoutLiveRules.Should().ContainSingle();

        (await CloseAsync(tenantId, ReconciliationEntryKind.Transaction, txId, "Immaterial change."))
            .IsSuccess.Should().BeTrue();
        (await CloseAsync(tenantId, ReconciliationEntryKind.Plan, planId, "Superseded by the 2027 plan."))
            .IsSuccess.Should().BeTrue();

        var after = await DashboardAsync(tenantId);
        after.ActionBand.DriftAlerts.Should().BeEmpty();
        after.ActionBand.PlansWithoutLiveRules.Should().BeEmpty();
    }


    /// <summary>
    /// ★★ THE CARD AND THE LIST BEHIND IT STAY EQUAL, WHICH IS WHY THE EXCLUSION WENT INSIDE THE
    /// SPEC. <c>UnprocessablePendingSpec</c> serves both the dashboard count and the Transactions
    /// list's <c>?attentionReason=</c> filter, and its own doc comment promises the two never
    /// disagree. Applying the closure at the dashboard call site would have kept that promise only
    /// until somebody clicked through; applying it in the spec means neither caller can forget.
    /// </summary>
    [Fact]
    public async Task A_closed_unprocessable_transaction_leaves_the_dashboard_card_and_the_attention_filter_together()
    {
        var tenantId = Guid.NewGuid();
        Guid closedId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var closing = Tx(tenantId, "REF-CARD-CLOSED", null, 2_000m);
            closedId = closing.Id;
            db.CompensationTransactions.Add(closing);
            db.CompensationTransactions.Add(Tx(tenantId, "REF-CARD-OPEN", null, 1_000m));
            await db.SaveChangesAsync();
        }

        var before = await DashboardAsync(tenantId);
        NoPayeeCount(before).Should().Be(2);
        (await AttentionFilterCountAsync(tenantId, ReconciliationReason.NoPayee)).Should().Be(2);

        (await CloseAsync(tenantId, ReconciliationEntryKind.Transaction, closedId, "Legacy import; no payee exists."))
            .IsSuccess.Should().BeTrue();

        var after = await DashboardAsync(tenantId);
        NoPayeeCount(after).Should().Be(1, "the card stops counting what somebody closed");
        (await AttentionFilterCountAsync(tenantId, ReconciliationReason.NoPayee))
            .Should().Be(1, "and the list it deep-links to shows exactly that many rows");

        (await RunAsync(tenantId)).Items.Should().ContainSingle("the Centre agrees with both");
    }

    /// <summary>
    /// ★ The ambiguity panel counts TRANSACTIONS per payee, so a closed row must not contribute to
    /// its payee's number. Excluded in the SQL projection rather than in the in-memory matcher: a
    /// closed row never reaches the engine's Candidates rule at all.
    /// </summary>
    [Fact]
    public async Task A_closed_ambiguous_transaction_stops_counting_on_the_dashboard()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        Guid txId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId, "EMP-AMB"));

            // Two Active assignments covering the same date, both in the transaction's currency:
            // the engine's own definition of an ambiguous attribution.
            var planA = MakePlan(tenantId, Guid.NewGuid(), "Plan A");
            var planB = MakePlan(tenantId, Guid.NewGuid(), "Plan B");
            planA.Activate("test", Now, Guid.NewGuid());
            planB.Activate("test", Now, Guid.NewGuid());
            db.CompensationPlans.AddRange(planA, planB);

            foreach (var plan in new[] { planA, planB })
            {
                db.PlanAssignments.Add(PlanAssignment.Create(
                    tenantId, plan.Id, payeeId,
                    PayeeReference.Snapshot(payeeId, "Queue Payee", "EMP-AMB"),
                    DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                    "test", Guid.NewGuid(), Now, Guid.NewGuid()));
            }

            var tx = Tx(tenantId, "REF-AMBIG", payeeId, 3_000m);
            txId = tx.Id;
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();
        }

        (await DashboardAsync(tenantId)).ActionBand.AmbiguousAttributionPayees
            .Should().ContainSingle();

        (await CloseAsync(tenantId, ReconciliationEntryKind.Transaction, txId, "Attributed by hand offline."))
            .IsSuccess.Should().BeTrue();

        (await DashboardAsync(tenantId)).ActionBand.AmbiguousAttributionPayees
            .Should().BeEmpty("a payee whose only ambiguous sale was closed has nothing left to warn about");
    }

    private static int NoPayeeCount(DashboardSummaryDto dashboard) =>
        dashboard.ActionBand.UnprocessablePendingItems
            .Where(i => i.Reason == ReconciliationReason.NoPayee)
            .Select(i => i.Count)
            .FirstOrDefault();

    /// <summary>The rows the Transactions list returns for a "needs attention" deep link.</summary>
    private async Task<int> AttentionFilterCountAsync(Guid tenantId, string reason)
    {
        await using var db = fixture.CreateDbForTenant(tenantId);
        var query = UnprocessablePendingSpec.ForReason(db, reason);
        query.Should().NotBeNull();
        return await query!.CountAsync();
    }


    // ══ KAN-54: los conteos del sidebar ══════════════════════════════════════════════════════

    private sealed class PermissionSetAuthorization(params string[] granted)
        : Wasnie.Application.Common.Interfaces.IAuthorizationService
    {
        private readonly HashSet<string> _granted = new(granted, StringComparer.Ordinal);

        public Task RequireAsync(string permission, CancellationToken ct = default) =>
            _granted.Contains(permission)
                ? Task.CompletedTask
                : throw new Wasnie.Application.Common.Exceptions.ForbiddenException(permission);

        public Task<bool> HasAsync(string permission, CancellationToken ct = default) =>
            Task.FromResult(_granted.Contains(permission));
    }

    /// <summary>
    /// A sender that answers the terminated-accounts query with a fixed result, so this test can drive
    /// the badge without standing up that whole handler and its access guard — which has its own tests.
    /// </summary>
    private sealed class StubTerminatedSender(int rowCount) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            var rows = Enumerable.Range(0, rowCount)
                .Select(i => new TerminatedPayeeBalanceDto(
                    Guid.NewGuid(), $"Payee {i}", $"EMP-{i}", new DateOnly(2026, 1, 1),
                    0m, "EUR", null, null, 0m, []))
                .ToList();

            var result = Result<TerminatedAccountsDto>.Success(new TerminatedAccountsDto(rows, []));
            return Task.FromResult((TResponse)(object)result);
        }

        public Task<object?> Send(object request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private async Task<SidebarBadgesDto> BadgesAsync(
        Guid tenantId, int terminatedRows, params string[] permissions)
    {
        await using var db = fixture.CreateDbForTenant(tenantId);
        var handler = new GetSidebarBadgesHandler(
            db, new StubTerminatedSender(terminatedRows), new PermissionSetAuthorization(permissions));

        var result = await handler.Handle(new GetSidebarBadgesQuery(), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }

    /// <summary>
    /// ★★ THE ACCEPTANCE CRITERION THAT MATTERS MOST: the badge and the Centre agree. They agree
    /// because they are the SAME query — this asserts it on real data rather than on inspection, so a
    /// future edit to one that forgets the other shows up here.
    /// </summary>
    [Fact]
    public async Task The_sidebar_count_equals_the_reconciliation_centre_count()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationTransactions.Add(Tx(tenantId, "REF-BADGE-1", null, 1_000m));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-BADGE-2", null, 2_000m));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-BADGE-3", null, 3_000m));
            await db.SaveChangesAsync();
        }

        var centre = await RunAsync(tenantId);
        var badges = await BadgesAsync(tenantId, terminatedRows: 0, Permission.ReportsViewAll);

        centre.Summary.TotalRows.Should().Be(3);
        badges.Reconciliation.Should().Be(centre.Summary.TotalRows);
    }

    /// <summary>
    /// ★ A CLOSED ROW LEAVES THE BADGE TOO. It comes for free from sharing the query — which is the
    /// whole argument for sharing it — but "for free" is exactly the kind of claim worth pinning.
    ///
    /// ★★ IT USES A DEAL-LOST ROW ON PURPOSE, AND THE FIRST VERSION DID NOT. Closure exclusion lives
    /// in TWO layers: inside UnprocessablePendingSpec for the pending-transaction reasons (so the
    /// dashboard card and the Transactions filter inherit it), and in ReconciliationQuery.ExcludeClosed
    /// for everything else. A test built on NoPayee therefore passes even if the badge skips
    /// ExcludeClosed entirely — it did, and the mutation went unnoticed until it was tried. Deal-lost
    /// depends only on ExcludeClosed, so this actually exercises the layer the badge relies on.
    /// </summary>
    [Fact]
    public async Task Closing_a_row_lowers_the_badge()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        Guid txId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId, "EMP-BADGE"));

            var lost = PaidTx(tenantId, "REF-BADGE-CLOSE", payeeId, 9_000m);
            txId = lost.Id;
            db.CompensationTransactions.Add(lost);
            db.DealLostAlerts.Add(DealLostAlert.Create(
                Guid.NewGuid(), tenantId, "HubSpot", "deal-badge", lost.Id, "REF-BADGE-CLOSE",
                CompensationTransactionStatus.Paid, 450m, EUR, Now, "sync"));

            // A second, untouched row so the badge has something left to report.
            db.CompensationTransactions.Add(Tx(tenantId, "REF-BADGE-KEEP", null, 500m));
            await db.SaveChangesAsync();
        }

        (await BadgesAsync(tenantId, 0, Permission.ReportsViewAll)).Reconciliation.Should().Be(2);

        var closed = await CloseAsync(
            tenantId, ReconciliationEntryKind.Transaction, txId,
            "Commission already paid; the clawback is applied.");
        closed.IsSuccess.Should().BeTrue(closed.Error);

        (await BadgesAsync(tenantId, 0, Permission.ReportsViewAll)).Reconciliation.Should().Be(1);
    }

    /// <summary>
    /// ★★ NO PERMISSION MEANS NO BADGE — null, NOT ZERO. A 0 would tell a user who may not see the
    /// queue that it is empty, which is a statement about the tenant's money they were not cleared to
    /// receive. The other badge still arrives: one missing permission removes a part of the answer,
    /// never the whole of it.
    /// </summary>
    [Fact]
    public async Task A_user_without_the_permission_gets_no_reconciliation_badge_at_all()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationTransactions.Add(Tx(tenantId, "REF-BADGE-HIDDEN", null, 4_000m));
            await db.SaveChangesAsync();
        }

        // Holds Ledger.Read but not Reports.ViewAll.
        var badges = await BadgesAsync(tenantId, terminatedRows: 2, Permission.LedgerRead);

        badges.Reconciliation.Should().BeNull("a 0 would describe money this user may not see");
        badges.TerminatedAccounts.Should().Be(2, "the permission they DO hold still answers");
        badges.FinancialsTotal.Should().Be(2, "the group total counts only what is visible");
    }

    [Fact]
    public async Task The_financials_total_adds_the_badges_the_user_can_see()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationTransactions.Add(Tx(tenantId, "REF-BADGE-T1", null, 100m));
            db.CompensationTransactions.Add(Tx(tenantId, "REF-BADGE-T2", null, 200m));
            await db.SaveChangesAsync();
        }

        var badges = await BadgesAsync(
            tenantId, terminatedRows: 3, Permission.ReportsViewAll, Permission.LedgerRead);

        badges.Reconciliation.Should().Be(2);
        badges.TerminatedAccounts.Should().Be(3);
        badges.FinancialsTotal.Should().Be(5);
    }

    /// <summary>
    /// ★ Each tenant counts only its own. The query filter does this, but the badge is a number a user
    /// reads without context, so a leak here would be believed.
    /// </summary>
    [Fact]
    public async Task Each_tenant_counts_only_its_own_rows()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantA))
        {
            db.CompensationTransactions.Add(Tx(tenantA, "REF-A-1", null, 100m));
            db.CompensationTransactions.Add(Tx(tenantA, "REF-A-2", null, 200m));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantB))
        {
            db.CompensationTransactions.Add(Tx(tenantB, "REF-B-1", null, 300m));
            await db.SaveChangesAsync();
        }

        (await BadgesAsync(tenantA, 0, Permission.ReportsViewAll)).Reconciliation.Should().Be(2);
        (await BadgesAsync(tenantB, 0, Permission.ReportsViewAll)).Reconciliation.Should().Be(1);
    }

    private sealed class FixedCurrentUser : Wasnie.Application.Common.Interfaces.ICurrentUserService
    {
        public string? UserId => "closer-user";
        public string? Email => "closer@test.com";
        public bool IsAuthenticated => true;
    }

    private sealed class AlwaysAllowAuthorization : Wasnie.Application.Common.Interfaces.IAuthorizationService
    {
        public Task RequireAsync(string permission, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(true);
    }
}
