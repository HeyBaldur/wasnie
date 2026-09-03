using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
    private sealed class AlwaysAllowAuthorization : Wasnie.Application.Common.Interfaces.IAuthorizationService
    {
        public Task RequireAsync(string permission, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(true);
    }
}
