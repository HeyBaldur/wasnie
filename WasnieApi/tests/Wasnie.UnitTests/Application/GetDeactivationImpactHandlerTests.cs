using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Handlers.Assignments;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The warning shown before an assignment is deactivated: how much unpaid commission it would strand.
///
/// ★★ WHY IT IS MONEY CODE. The figure goes on a confirmation dialog, and a wrong one is worse than
/// none: too high and the reader stops believing the dialog, too low and they switch off the link that
/// was the only route €385,731.02 had to a pay run — which is the incident this exists to prevent.
/// </summary>
public sealed class GetDeactivationImpactHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly GetDeactivationImpactHandler _handler;

    public GetDeactivationImpactHandlerTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        tenantCtx.IsResolved.Returns(true);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        var auth = Substitute.For<IAuthorizationService>();
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _handler = new GetDeactivationImpactHandler(_db, auth);
    }

    public void Dispose() => _db.Dispose();

    // ── seeding ───────────────────────────────────────────────────────────────

    private Guid SeedPayee(string name)
    {
        var payee = Payee.Create(TenantId, name, $"EMP-{Guid.NewGuid():N}"[..12], null, null,
            "seed", Guid.NewGuid(), Now);
        _db.Payees.Add(payee);
        _db.SaveChanges();
        return payee.Id;
    }

    private Guid SeedPlan(string name)
    {
        var plan = CompensationPlan.Create(TenantId, name, "seeded",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), "EUR",
            "seed", Guid.NewGuid(), Now, Guid.NewGuid());
        _db.CompensationPlans.Add(plan);
        _db.SaveChanges();
        return plan.Id;
    }

    private Guid SeedAssignment(Guid payeeId, Guid planId, bool active = true)
    {
        var a = PlanAssignment.Create(TenantId, planId, payeeId,
            PayeeReference.Snapshot(payeeId, "Payee", "EMP-1"),
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "seed", Guid.NewGuid(), Now, Guid.NewGuid());
        if (!active) a.Deactivate("seed", Now, Guid.NewGuid());
        _db.PlanAssignments.Add(a);
        _db.SaveChanges();
        return a.Id;
    }

    private Credit SeedCredit(Guid payeeId, Guid planId, decimal amount, string currency = "EUR")
    {
        var ruleId = Guid.NewGuid();
        var credit = Credit.Allocate(
            TenantId, Guid.NewGuid(), payeeId, planId, ruleId,
            RuleSnapshot.Freeze(ruleId, planId, 1, "Base", RateTable.Flat(0.05m), Trigger.Always(), Now),
            Money.Of(amount * 20m, currency), Money.Of(amount, currency),
            Percentage.FromPercent(5m), CreditRole.Primary,
            "seed", Guid.NewGuid(), Now, Guid.NewGuid());
        _db.Credits.Add(credit);
        _db.SaveChanges();
        return credit;
    }

    private Task<Wasnie.Domain.Common.Results.Result<Wasnie.Application.Compensation.DTOs.DeactivationImpactDto>>
        Ask(params Guid[] ids) => _handler.Handle(new GetDeactivationImpactQuery(ids), default);

    // ── the number itself ─────────────────────────────────────────────────────

    [Fact]
    public async Task ItSumsTheUnpaidCommissionThatWouldStopBeingPayable()
    {
        var payeeId = SeedPayee("Rudolph");
        var planId = SeedPlan("EU Accelerator");
        var assignmentId = SeedAssignment(payeeId, planId);
        SeedCredit(payeeId, planId, 19_481.02m);
        SeedCredit(payeeId, planId, 480.00m);

        var result = await Ask(assignmentId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.StrandedByCurrency.Should().ContainSingle()
            .Which.Amount.Should().Be(19_961.02m);
        result.Value.Items.Should().ContainSingle()
            .Which.CreditCount.Should().Be(2);
    }

    [Fact]
    public async Task ItNamesThePayeeAndThePlan()
    {
        // "3 assignments" is not something anybody can act on; "Rudolph, EU Accelerator" is.
        var payeeId = SeedPayee("Rudolph GeHard Chipellin");
        var planId = SeedPlan("EU Accelerator");
        var assignmentId = SeedAssignment(payeeId, planId);
        SeedCredit(payeeId, planId, 100m);

        var item = (await Ask(assignmentId)).Value!.Items.Single();

        item.PayeeName.Should().Be("Rudolph GeHard Chipellin");
        item.PlanName.Should().Be("EU Accelerator");
    }

    [Fact]
    public async Task PaidCommissionIsNotAtRisk()
    {
        // It already left. Counting it would inflate the warning on every mature assignment.
        var payeeId = SeedPayee("Ada");
        var planId = SeedPlan("Core");
        var assignmentId = SeedAssignment(payeeId, planId);

        var paid = SeedCredit(payeeId, planId, 5_000m);
        paid.Consume(Guid.NewGuid(), Now, Guid.NewGuid());
        SeedCredit(payeeId, planId, 250m);
        _db.SaveChanges();

        var result = await Ask(assignmentId);

        result.Value!.StrandedByCurrency.Single().Amount.Should().Be(250m);
    }

    [Fact]
    public async Task ClosedCommissionIsNotAtRisk()
    {
        // Written off or settled outside Wasnie: neither paid nor owed, so deactivating strands nothing.
        var payeeId = SeedPayee("Ada");
        var planId = SeedPlan("Core");
        var assignmentId = SeedAssignment(payeeId, planId);

        var closed = SeedCredit(payeeId, planId, 900m);
        closed.Close(CreditClosureReason.WrittenOff, "n", "seed", Now, Guid.NewGuid());
        _db.SaveChanges();

        (await Ask(assignmentId)).Value!.StrandedByCurrency.Should().BeEmpty();
    }

    [Fact]
    public async Task EachCurrencyIsReportedSeparately()
    {
        // Adding 100 EUR to 100 USD would print a number that is true in no currency at all.
        var payeeId = SeedPayee("Ada");
        var planId = SeedPlan("Core");
        var assignmentId = SeedAssignment(payeeId, planId);
        SeedCredit(payeeId, planId, 100m, "EUR");
        SeedCredit(payeeId, planId, 100m, "USD");

        var totals = (await Ask(assignmentId)).Value!.StrandedByCurrency;

        totals.Should().HaveCount(2);
        totals.Select(t => t.Currency).Should().BeEquivalentTo(["EUR", "USD"]);
    }

    // ── when there is nothing to warn about ───────────────────────────────────

    [Fact]
    public async Task AnAssignmentWithNoUnpaidCommissionStrandsNothing()
    {
        var payeeId = SeedPayee("Ada");
        var planId = SeedPlan("Core");
        var assignmentId = SeedAssignment(payeeId, planId);

        var result = await Ask(assignmentId);

        result.Value!.StrandedByCurrency.Should().BeEmpty();
        result.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task DeactivatingWhatIsAlreadyDeactivatedStrandsNothing()
    {
        // The money is already unreachable; the act about to be confirmed changes nothing.
        var payeeId = SeedPayee("Ada");
        var planId = SeedPlan("Core");
        var assignmentId = SeedAssignment(payeeId, planId, active: false);
        SeedCredit(payeeId, planId, 1_000m);

        (await Ask(assignmentId)).Value!.Items.Should().BeEmpty();
    }

    /// <summary>
    /// ★ THE SUBTLETY. A renewal overlapping the assignment it replaces is ordinary. Warning when
    /// another Active link survives would teach the reader to click through the dialog that one day
    /// is telling the truth.
    /// </summary>
    [Fact]
    public async Task ASurvivingActiveAssignmentToTheSamePlanMeansNothingIsStranded()
    {
        var payeeId = SeedPayee("Ada");
        var planId = SeedPlan("Core");
        var oldOne = SeedAssignment(payeeId, planId);
        SeedAssignment(payeeId, planId);   // the renewal, left Active
        SeedCredit(payeeId, planId, 1_000m);

        (await Ask(oldOne)).Value!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task DeactivatingBothLinksDoesStrandTheMoney()
    {
        // Same fixture as above, but the survivor goes too — so the warning must appear, ONCE.
        var payeeId = SeedPayee("Ada");
        var planId = SeedPlan("Core");
        var a1 = SeedAssignment(payeeId, planId);
        var a2 = SeedAssignment(payeeId, planId);
        SeedCredit(payeeId, planId, 1_000m);

        var result = await Ask(a1, a2);

        result.Value!.Items.Should().ContainSingle();
        result.Value.StrandedByCurrency.Single().Amount.Should().Be(1_000m);
    }

    [Fact]
    public async Task AnotherPlansUnpaidCommissionIsNotCounted()
    {
        // The engine gates per plan. Pulling in a plan that is not being touched would overstate the
        // consequence of the click by whatever that other plan owes.
        var payeeId = SeedPayee("Ada");
        var touched = SeedPlan("Core");
        var untouched = SeedPlan("Bonus");
        var assignmentId = SeedAssignment(payeeId, touched);
        SeedAssignment(payeeId, untouched);
        SeedCredit(payeeId, touched, 300m);
        SeedCredit(payeeId, untouched, 9_000m);

        (await Ask(assignmentId)).Value!.StrandedByCurrency.Single().Amount.Should().Be(300m);
    }

    [Fact]
    public async Task AnotherPayeesCommissionOnTheSamePlanIsNotCounted()
    {
        var ada = SeedPayee("Ada");
        var bob = SeedPayee("Bob");
        var planId = SeedPlan("Core");
        var adaAssignment = SeedAssignment(ada, planId);
        SeedAssignment(bob, planId);
        SeedCredit(ada, planId, 300m);
        SeedCredit(bob, planId, 7_000m);

        var result = await Ask(adaAssignment);

        result.Value!.StrandedByCurrency.Single().Amount.Should().Be(300m);
        result.Value.Items.Single().PayeeName.Should().Be("Ada");
    }

    [Fact]
    public async Task AnEmptySelectionAsksNothingOfTheDatabase()
    {
        var result = await Ask();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
    }

    // ── the bulk path, which is the dangerous one ─────────────────────────────

    [Fact]
    public async Task TheBulkPathReportsEveryPayeeItWouldStrand()
    {
        var ada = SeedPayee("Ada");
        var bob = SeedPayee("Bob");
        var planId = SeedPlan("Core");
        var a = SeedAssignment(ada, planId);
        var b = SeedAssignment(bob, planId);
        SeedCredit(ada, planId, 300m);
        SeedCredit(bob, planId, 700m);

        var result = await Ask(a, b);

        result.Value!.Items.Should().HaveCount(2);
        result.Value.StrandedByCurrency.Single().Amount.Should().Be(1_000m);
    }

    [Fact]
    public async Task TheBiggestAmountIsListedFirst()
    {
        // The dialog shows a few rows; the one worth stopping for has to be one of them.
        var ada = SeedPayee("Ada");
        var bob = SeedPayee("Bob");
        var planId = SeedPlan("Core");
        var a = SeedAssignment(ada, planId);
        var b = SeedAssignment(bob, planId);
        SeedCredit(ada, planId, 300m);
        SeedCredit(bob, planId, 700m);

        var items = (await Ask(a, b)).Value!.Items;

        items.First().PayeeName.Should().Be("Bob");
    }
}
