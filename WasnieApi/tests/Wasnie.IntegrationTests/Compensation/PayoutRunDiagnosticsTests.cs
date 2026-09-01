#pragma warning disable CS8602

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Application.Compensation.Handlers.Payouts;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.Compensation;

/// <summary>
/// The counters that let a zero be explained instead of guessed at.
///
/// ★★ WHY THEY EXIST. The engine's result was a bare <c>PayoutsCreated</c>, and the screen turned a zero
/// into "No payouts created. No matching credits found for this period." — a cause the backend never
/// established. In the run that prompted this, that sentence was false twice over: four assignments were
/// dropped because their payee had left, all twenty survivors hit an already-Paid payout, and the engine
/// NEVER QUERIED A CREDIT. An administrator moved date ranges for three attempts because of it
/// (docs/DIAG_POL-8554_PAYOUT_Y_CREDITOS_INVENTADOS.md).
///
/// ★ NOTHING HERE CHANGES ELIGIBILITY. Every discard asserted below already happened exactly this way;
/// these tests pin the REPORT of it. If one of them starts failing because a payout appeared where none
/// did before, the bug is that someone changed who gets paid inside a reporting change.
/// </summary>
[Collection(PayoutEngineCollection.Name)]
public sealed class PayoutRunDiagnosticsTests(PayoutEngineFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    private static readonly DateOnly PeriodStart = new(2026, 6, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 6, 30);

    private static CalculatePayoutsForPeriodHandler CreateHandler(ApplicationDbContext db, Guid tenantId) =>
        new(db, new PayoutEngineFixture.FixedTenantContext(tenantId),
            new FixedCurrentUser(), new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            NullLogger<CalculatePayoutsForPeriodHandler>.Instance);

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public string? UserId => "test-user";
        public string? Email => "test@test.com";
        public bool IsAuthenticated => true;
    }

    private static Plan MakePlan(Guid tenantId, Guid planId)
    {
        var plan = Plan.Create(tenantId, "Plan", "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), Eur,
            "test", planId, Now, Guid.NewGuid());
        plan.AddRule("Commission", 1,
            new Measurement
            {
                Type = MeasurementType.Revenue,
                SourceField = "amount",
                Aggregation = MeasurementAggregation.Sum,
            },
            RateTable.Flat(0.10m));
        return plan;
    }

    /// <summary>One payee assigned to one plan for the whole of June. The baseline every test bends.</summary>
    private static Payee SeedAssignedPayee(
        ApplicationDbContext db, Guid tenantId, Guid planId, string code, bool terminated = false)
    {
        var payee = Payee.Create(tenantId, $"Payee {code}", code, $"{code}@test.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        if (terminated)
            payee.MarkAsTerminated(new DateOnly(2026, 5, 31), "hr@test.com", Now);
        db.Payees.Add(payee);

        db.PlanAssignments.Add(PlanAssignment.Create(
            tenantId, planId, payee.Id,
            PayeeReference.Snapshot(payee.Id, payee.FullName, code),
            DateRange.Of(PeriodStart, PeriodEnd), "test", Guid.NewGuid(), Now, Guid.NewGuid()));

        return payee;
    }

    /// <summary>An already-Paid payout covering exactly the run's period — the conflict gate's trigger.</summary>
    private static void SeedPaidPayout(ApplicationDbContext db, Guid tenantId, Payee payee, Guid planId)
    {
        var payout = CompensationPayout.Calculate(
            tenantId, payee.Id, planId,
            PayeeReference.Snapshot(payee.Id, payee.FullName, payee.EmployeeCode),
            DateRange.Of(PeriodStart, PeriodEnd), [], Eur, "test",
            Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);
        payout.Approve("test", Now, Guid.NewGuid());
        payout.MarkPaid("test", Now);
        db.CompensationPayouts.Add(payout);
    }

    private static int SkipCount(PayoutRunDiagnostics d, string code) =>
        d.Skipped.FirstOrDefault(s => s.Code == code)?.Count ?? 0;

    // ══ One test per discard point ════════════════════════════════════════════

    [Fact]
    public async Task A_terminated_payee_is_counted_under_its_own_reason()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationPlans.Add(MakePlan(tenantId, planId));
            SeedAssignedPayee(db, tenantId, planId, "GONE", terminated: true);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await CreateHandler(db, tenantId).Handle(
                new CalculatePayoutsForPeriodCommand(PeriodStart, PeriodEnd), CancellationToken.None);

            var d = result.Value.Diagnostics;
            d.AssignmentsConsidered.Should().Be(1);
            SkipCount(d, PayoutSkipReason.TerminatedPayee).Should().Be(1);
            d.AssignmentsReachingCreditLookup.Should().Be(0,
                "the engine stopped before it had any reason to look at credits");
        }
    }

    [Fact]
    public async Task An_existing_paid_payout_is_counted_under_its_own_reason()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationPlans.Add(MakePlan(tenantId, planId));
            var payee = SeedAssignedPayee(db, tenantId, planId, "PAID");
            SeedPaidPayout(db, tenantId, payee, planId);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await CreateHandler(db, tenantId).Handle(
                new CalculatePayoutsForPeriodCommand(PeriodStart, PeriodEnd), CancellationToken.None);

            var d = result.Value.Diagnostics;
            d.AssignmentsConsidered.Should().Be(1);
            SkipCount(d, PayoutSkipReason.ExistingPayout).Should().Be(1);
            result.Value.Conflicts.Should().ContainSingle("the row-level detail is still reported too");
        }
    }

    [Fact]
    public async Task An_archived_plan_is_counted_under_its_own_reason()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlan(tenantId, planId);
            plan.Activate("test", Now, Guid.NewGuid());
            plan.Archive("test", Now, Guid.NewGuid());
            db.CompensationPlans.Add(plan);
            SeedAssignedPayee(db, tenantId, planId, "ARCH");
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await CreateHandler(db, tenantId).Handle(
                new CalculatePayoutsForPeriodCommand(PeriodStart, PeriodEnd), CancellationToken.None);

            var d = result.Value.Diagnostics;
            SkipCount(d, PayoutSkipReason.PlanNotPayable).Should().Be(1);
            d.AssignmentsReachingCreditLookup.Should().Be(0);
        }
    }

    /// <summary>
    /// Nothing to consider is a DIFFERENT answer from considered-and-discarded, and the screen picks a
    /// different sentence for each. Collapsing them is how "0 payouts" became a guess in the first place.
    /// </summary>
    [Fact]
    public async Task A_run_with_no_assignments_at_all_reports_nothing_considered_and_no_reasons()
    {
        var tenantId = Guid.NewGuid();

        await using var db = fixture.CreateDbForTenant(tenantId);
        var result = await CreateHandler(db, tenantId).Handle(
            new CalculatePayoutsForPeriodCommand(PeriodStart, PeriodEnd), CancellationToken.None);

        var d = result.Value.Diagnostics;
        d.AssignmentsConsidered.Should().Be(0);
        d.Skipped.Should().BeEmpty("nothing was discarded — there was nothing to discard");
        d.AssignmentsReachingCreditLookup.Should().Be(0);
        d.CreditsExamined.Should().Be(0);
    }

    /// <summary>Reasons that discarded nothing are absent, not present as zeros.</summary>
    [Fact]
    public async Task Reasons_that_discarded_nothing_are_omitted_rather_than_reported_as_zero()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationPlans.Add(MakePlan(tenantId, planId));
            SeedAssignedPayee(db, tenantId, planId, "GONE-ONLY", terminated: true);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await CreateHandler(db, tenantId).Handle(
                new CalculatePayoutsForPeriodCommand(PeriodStart, PeriodEnd), CancellationToken.None);

            result.Value.Diagnostics.Skipped.Should().ContainSingle()
                .Which.Code.Should().Be(PayoutSkipReason.TerminatedPayee);
        }
    }

    // ══ ★ The run that started all of this ════════════════════════════════════

    /// <summary>
    /// ★★ THE REAL JUNE RUN, REBUILT. 24 assignments considered, 4 dropped for terminated payees, the
    /// remaining 20 blocked by an already-Paid payout — and NOT ONE CREDIT LOOKED AT. That last number
    /// is the whole point: while it is zero, "no matching credits found for this period" is not a
    /// statement anybody is entitled to make.
    /// </summary>
    [Fact]
    public async Task The_june_run_reports_four_terminated_twenty_conflicts_and_no_credits_examined()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationPlans.Add(MakePlan(tenantId, planId));

            for (var i = 0; i < 4; i++)
                SeedAssignedPayee(db, tenantId, planId, $"GONE-{i:D2}", terminated: true);

            for (var i = 0; i < 20; i++)
            {
                var payee = SeedAssignedPayee(db, tenantId, planId, $"PAID-{i:D2}");
                SeedPaidPayout(db, tenantId, payee, planId);
            }

            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await CreateHandler(db, tenantId).Handle(
                new CalculatePayoutsForPeriodCommand(PeriodStart, PeriodEnd), CancellationToken.None);

            result.Value.PayoutsCreated.Should().Be(0);

            var d = result.Value.Diagnostics;
            d.AssignmentsConsidered.Should().Be(24);
            SkipCount(d, PayoutSkipReason.TerminatedPayee).Should().Be(4);
            SkipCount(d, PayoutSkipReason.ExistingPayout).Should().Be(20);
            d.AssignmentsReachingCreditLookup.Should().Be(0);
            d.CreditsExamined.Should().Be(0,
                "★ the engine never queried a credit, so the old message was false about its own subject");
        }
    }

    /// <summary>
    /// A run that discards everything on conflicts must not produce a message about credits or dates.
    /// Expressed as the data the screen has: no credit was examined, so it has nothing to say about them.
    /// </summary>
    [Fact]
    public async Task A_run_blocked_entirely_by_conflicts_says_nothing_about_credits()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationPlans.Add(MakePlan(tenantId, planId));
            for (var i = 0; i < 3; i++)
            {
                var payee = SeedAssignedPayee(db, tenantId, planId, $"BLOCK-{i}");
                SeedPaidPayout(db, tenantId, payee, planId);
            }

            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await CreateHandler(db, tenantId).Handle(
                new CalculatePayoutsForPeriodCommand(PeriodStart, PeriodEnd), CancellationToken.None);

            var d = result.Value.Diagnostics;
            d.AssignmentsConsidered.Should().Be(3);
            d.AssignmentsReachingCreditLookup.Should().Be(0);
            d.CreditsExamined.Should().Be(0);
            SkipCount(d, PayoutSkipReason.ExistingPayout).Should().Be(3);
        }
    }

    // ══ The happy path still reports honestly ═════════════════════════════════

    /// <summary>
    /// A run that DOES reach the credit lookup says so. Without this, "reached zero" would be
    /// indistinguishable from a counter nobody ever increments.
    /// </summary>
    [Fact]
    public async Task A_run_that_processes_an_assignment_reports_that_it_looked_at_credits()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationPlans.Add(MakePlan(tenantId, planId));
            SeedAssignedPayee(db, tenantId, planId, "OK");
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await CreateHandler(db, tenantId).Handle(
                new CalculatePayoutsForPeriodCommand(PeriodStart, PeriodEnd), CancellationToken.None);

            var d = result.Value.Diagnostics;
            d.AssignmentsConsidered.Should().Be(1);
            d.AssignmentsReachingCreditLookup.Should().Be(1);
            d.Skipped.Should().BeEmpty();
            result.Value.PayoutsCreated.Should().Be(1, "eligibility is unchanged by this work");
        }
    }
}
