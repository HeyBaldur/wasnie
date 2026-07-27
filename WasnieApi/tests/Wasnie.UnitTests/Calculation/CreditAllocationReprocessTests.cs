using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.Builders;
using Wasnie.UnitTests.TestDoubles;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// Anti-double-pay moved from "this transaction was already processed" to "this (transaction, plan,
/// rule) already has a live credit" — the same key as UX_Credits_Tenant_Transaction_Plan_Rule_Live.
///
/// Re-processing must stay a no-op (that guarantee is non-negotiable), while two DIFFERENT rules
/// matching one transaction must each keep their own credit: stacked base + SPIFF is intentional
/// concurrency, not double payment.
/// </summary>
public sealed class CreditAllocationReprocessTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PayeeId = Guid.NewGuid();
    private static readonly DateOnly TxDate = new(2026, 6, 15);
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        CreditAllocationService Service,
        CompensationPlan Plan,
        IReadOnlyDictionary<Guid, IReadOnlyList<PlanAssignment>> AssignmentsByPayee,
        IReadOnlyDictionary<Guid, CompensationPlan> PlansById);

    /// <param name="ruleCount">Rules on the plan — 2 models a base rule plus a stacked SPIFF.</param>
    private static Fixture BuildFixture(string dbName, int ruleCount = 1)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var plan = new PlanBuilder()
            .WithTenantId(TenantId).WithName("Plan").WithCurrency("EUR")
            .WithPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))
            .Build();

        for (var i = 0; i < ruleCount; i++)
            plan.AddRule(
                $"Rule {i + 1}", sortOrder: i + 1,
                measurement: new Measurement
                {
                    Type = MeasurementType.Revenue, SourceField = "amount",
                    Aggregation = MeasurementAggregation.Sum,
                },
                rateTable: RateTable.Flat(0.05m));

        var assignment = PlanAssignment.Create(
            TenantId, plan.Id, PayeeId,
            PayeeReference.Snapshot(PayeeId, "Test Payee", "E1"),
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "seed", Guid.NewGuid(), Now, Guid.NewGuid());

        var service = new CreditAllocationService(
            db, new FakeGuidGenerator(), new FakeClock(Now.UtcDateTime),
            NullLogger<CreditAllocationService>.Instance,
            Substitute.For<IQuotaAttainmentService>());

        return new Fixture(
            service, plan,
            new Dictionary<Guid, IReadOnlyList<PlanAssignment>> { [PayeeId] = [assignment] },
            new Dictionary<Guid, CompensationPlan> { [plan.Id] = plan });
    }

    private static CompensationTransaction MakeTransaction(Guid? selected = null) =>
        CompensationTransaction.Ingest(
            TenantId, "REF-REPROC-1", PayeeId, Money.Of(1000m, "EUR"), TxDate,
            TransactionSource.EtlImport, "import", Guid.NewGuid(), Now, Guid.NewGuid(),
            selectedPlanAssignmentId: selected);

    private static HashSet<(Guid, Guid, Guid)> KeysOf(CompensationTransaction tx, CompensationPlan plan) =>
        plan.Rules.Select(r => (tx.Id, plan.Id, r.Id)).ToHashSet();

    // (b) No regression: a fresh transaction allocates exactly as before.
    [Fact]
    public async Task A_transaction_with_no_existing_credits_allocates_normally()
    {
        var f = BuildFixture(nameof(A_transaction_with_no_existing_credits_allocates_normally));
        var tx = MakeTransaction();

        var credits = await f.Service.AllocateAsync(
            tx, f.AssignmentsByPayee, f.PlansById, new HashSet<(Guid, Guid, Guid)>());

        credits.Should().ContainSingle();
        credits[0].CreditedAmount.Amount.Should().Be(50m);
    }

    // (a) The non-negotiable guarantee: re-running over an allocated transaction adds nothing and
    // does not throw. It must never reach the unique index — that is a net, not flow control.
    [Fact]
    public async Task Re_processing_an_already_credited_transaction_is_a_no_op()
    {
        var f = BuildFixture(nameof(Re_processing_an_already_credited_transaction_is_a_no_op));
        var tx = MakeTransaction();

        var credits = await f.Service.AllocateAsync(
            tx, f.AssignmentsByPayee, f.PlansById, KeysOf(tx, f.Plan));

        credits.Should().BeEmpty();
    }

    // (c) The enabling behaviour: rule A already credited must not block rule B on the same
    // transaction and plan. The resolver does not produce this yet — the guard must not stand in
    // the way when it does.
    [Fact]
    public async Task A_second_rule_can_still_be_credited_when_the_first_already_was()
    {
        var f = BuildFixture(nameof(A_second_rule_can_still_be_credited_when_the_first_already_was), ruleCount: 2);
        var tx = MakeTransaction();
        var firstRule = f.Plan.Rules.First();

        // Only rule 1 is covered.
        var credits = await f.Service.AllocateAsync(
            tx, f.AssignmentsByPayee, f.PlansById,
            new HashSet<(Guid, Guid, Guid)> { (tx.Id, f.Plan.Id, firstRule.Id) });

        credits.Should().ContainSingle("the covered rule is skipped, the other one still credits");
        credits[0].RuleId.Should().NotBe(firstRule.Id);
    }

    // Both rules uncovered → both credit. Confirms multi-credit within a plan is untouched.
    [Fact]
    public async Task Two_uncovered_rules_both_credit_the_same_transaction()
    {
        var f = BuildFixture(nameof(Two_uncovered_rules_both_credit_the_same_transaction), ruleCount: 2);

        var credits = await f.Service.AllocateAsync(
            MakeTransaction(), f.AssignmentsByPayee, f.PlansById, new HashSet<(Guid, Guid, Guid)>());

        credits.Should().HaveCount(2);
        credits.Select(c => c.RuleId).Should().OnlyHaveUniqueItems();
    }

    // (d) RecalculateCredits supersedes then re-allocates. Superseded rows are absent from the live
    // key set, so the same (transaction, plan, rule) allocates again.
    [Fact]
    public async Task After_superseding_the_same_key_can_be_allocated_again()
    {
        var f = BuildFixture(nameof(After_superseding_the_same_key_can_be_allocated_again));
        var tx = MakeTransaction();

        // Superseded credits are excluded from the live set by LoadLiveCreditKeysAsync — modelled here
        // as an empty set, which is what that query returns once the row is superseded.
        var credits = await f.Service.AllocateAsync(
            tx, f.AssignmentsByPayee, f.PlansById, new HashSet<(Guid, Guid, Guid)>());

        credits.Should().ContainSingle();
    }

    // (e) An explicit manual attribution keeps working — the guard change does not touch resolution.
    [Fact]
    public async Task A_transaction_with_a_declared_plan_still_allocates()
    {
        var f = BuildFixture(nameof(A_transaction_with_a_declared_plan_still_allocates));
        var assignmentId = f.AssignmentsByPayee[PayeeId][0].Id;
        var tx = MakeTransaction(selected: assignmentId);

        var credits = await f.Service.AllocateAsync(
            tx, f.AssignmentsByPayee, f.PlansById, new HashSet<(Guid, Guid, Guid)>());

        credits.Should().ContainSingle();
        credits[0].PlanId.Should().Be(f.Plan.Id);
    }

    // ── Every eligible assignment contributes (Paso 3) ────────────────────────────────────────

    // A payee holding TWO active assignments to the SAME plan is real (3 payees do today). Both are
    // eligible, both evaluate the same rules — the in-pass key tracking must stop the second from
    // duplicating, or the unique index would reject it and the plan's attainment would double-count.
    [Fact]
    public async Task Two_assignments_to_the_same_plan_credit_it_only_once()
    {
        var f = BuildFixture(nameof(Two_assignments_to_the_same_plan_credit_it_only_once), ruleCount: 2);
        var first = f.AssignmentsByPayee[PayeeId][0];

        // A second, differently-dated but still covering, assignment to the very same plan.
        var second = PlanAssignment.Create(
            TenantId, f.Plan.Id, PayeeId,
            PayeeReference.Snapshot(PayeeId, "Test Payee", "E1"),
            DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 8, 31)),
            "seed", Guid.NewGuid(), Now, Guid.NewGuid());

        var twoAssignments = new Dictionary<Guid, IReadOnlyList<PlanAssignment>>
        {
            [PayeeId] = [first, second],
        };

        var credits = await f.Service.AllocateAsync(
            MakeTransaction(), twoAssignments, f.PlansById, new HashSet<(Guid, Guid, Guid)>());

        // Two rules on the plan → two credits total, NOT four.
        credits.Should().HaveCount(2);
        credits.Select(c => c.RuleId).Should().OnlyHaveUniqueItems();
    }

    // Re-processing a transaction that was credited by SEVERAL plans stays a no-op across all of them.
    [Fact]
    public async Task Re_processing_is_a_no_op_across_every_credited_plan()
    {
        var f = BuildFixture(nameof(Re_processing_is_a_no_op_across_every_credited_plan), ruleCount: 2);
        var tx = MakeTransaction();

        var first = await f.Service.AllocateAsync(
            tx, f.AssignmentsByPayee, f.PlansById, new HashSet<(Guid, Guid, Guid)>());
        first.Should().HaveCount(2);

        var covered = first.Select(c => (tx.Id, c.PlanId, c.RuleId)).ToHashSet();
        var second = await f.Service.AllocateAsync(tx, f.AssignmentsByPayee, f.PlansById, covered);

        second.Should().BeEmpty();
    }

    // The live-key loader must mirror the unique index filter: superseded out, consumed IN.
    [Fact]
    public async Task LoadLiveCreditKeys_excludes_superseded_but_keeps_consumed()
    {
        var f = BuildFixture(nameof(LoadLiveCreditKeys_excludes_superseded_but_keeps_consumed));
        var tx = MakeTransaction();

        var credits = await f.Service.AllocateAsync(
            tx, f.AssignmentsByPayee, f.PlansById, new HashSet<(Guid, Guid, Guid)>());
        var credit = credits.Single();

        // A consumed (already paid) credit must still occupy its key.
        credit.Consume(Guid.NewGuid(), Now, Guid.NewGuid());

        var db = (ApplicationDbContext)typeof(CreditAllocationService)
            .GetField("_db", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(f.Service)!;
        db.Credits.Add(credit);
        await db.SaveChangesAsync();

        var keys = await f.Service.LoadLiveCreditKeysAsync([tx.Id]);
        keys.Should().Contain((tx.Id, f.Plan.Id, credit.RuleId));

        credit.Supersede("recalculated", Now, Guid.NewGuid());
        await db.SaveChangesAsync();

        var afterSupersede = await f.Service.LoadLiveCreditKeysAsync([tx.Id]);
        afterSupersede.Should().BeEmpty();
    }
}
