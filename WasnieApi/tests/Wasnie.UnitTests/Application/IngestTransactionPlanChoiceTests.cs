using FluentAssertions;
using NSubstitute;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.Handlers.Transactions;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.Builders;
using Wasnie.UnitTests.TestDoubles;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The server-side gate: a manual transaction for a payee on 2+ applicable plans cannot be saved
/// without the admin stating which plan it belongs to. The form enforces the same rule, but this is
/// the authority — a client that omits the field must be rejected, not silently tie-broken.
/// </summary>
public sealed class IngestTransactionPlanChoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TxDate = new(2026, 6, 15);

    private sealed record Harness(
        ApplicationDbContext Db,
        IngestTransactionHandler Handler,
        Guid TenantId,
        Guid PayeeId,
        List<PlanAssignment> Assignments);

    /// <param name="planCount">How many applicable plans the payee is assigned to.</param>
    private static Harness BuildHarness(string dbName, int planCount)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        var payee = Payee.Create(
            tenantId, "Test Payee", "E1", "payee@test.com", null, "seed", Guid.NewGuid(), Now);
        db.Payees.Add(payee);

        var assignments = new List<PlanAssignment>();
        for (var i = 0; i < planCount; i++)
        {
            var plan = new PlanBuilder()
                .WithTenantId(tenantId).WithName($"Plan {i + 1}").WithCurrency("EUR")
                .WithPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))
                .Build();
            plan.AddRule(
                $"Rule {i + 1}", sortOrder: 1,
                measurement: new Measurement
                {
                    Type = MeasurementType.Revenue, SourceField = "amount",
                    Aggregation = MeasurementAggregation.Sum,
                },
                rateTable: RateTable.Flat(0.05m));
            db.CompensationPlans.Add(plan);

            var assignment = PlanAssignment.Create(
                tenantId, plan.Id, payee.Id,
                PayeeReference.Snapshot(payee.Id, "Test Payee", "E1"),
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                "seed", Guid.NewGuid(), Now, Guid.NewGuid());
            db.PlanAssignments.Add(assignment);
            assignments.Add(assignment);
        }

        db.SaveChanges();

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("admin");

        var fieldRequirements = Substitute.For<IFieldRequirementService>();
        fieldRequirements.IsRequiredAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Credit allocation itself is covered by CreditAllocationPlanChoiceTests; here the subject is
        // the ingest gate, so allocation is stubbed out.
        var credits = Substitute.For<ICreditAllocationService>();
        credits.AllocateAsync(Arg.Any<CompensationTransaction>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Credit>());

        var handler = new IngestTransactionHandler(
            db, tenantCtx, currentUser, new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            Substitute.For<IAuthorizationService>(), fieldRequirements, credits,
            new TransactionCreateGuard(db));

        return new Harness(db, handler, tenantId, payee.Id, assignments);
    }

    private static IngestTransactionCommand Command(
        Guid payeeId, Guid? selectedPlanAssignmentId, string reference = "REF-1") =>
        new(ReferenceNumber: reference,
            PayeeId: payeeId,
            Amount: 50m,
            Currency: "EUR",
            TransactionDate: TxDate,
            Quantity: 112,
            ProcessImmediately: false,
            Description: null,
            SelectedPlanAssignmentId: selectedPlanAssignmentId);

    // (a) The core rule: ambiguity must be resolved by a human, not by the engine's tie-break.
    [Fact]
    public async Task Payee_on_two_plans_is_rejected_when_no_plan_is_selected()
    {
        var h = BuildHarness(nameof(Payee_on_two_plans_is_rejected_when_no_plan_is_selected), planCount: 2);

        var result = await h.Handler.Handle(Command(h.PayeeId, selectedPlanAssignmentId: null), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("more than one applicable plan");
        (await h.Db.CompensationTransactions.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Payee_on_two_plans_succeeds_and_persists_the_selection()
    {
        var h = BuildHarness(nameof(Payee_on_two_plans_succeeds_and_persists_the_selection), planCount: 2);
        var chosen = h.Assignments[1];

        var result = await h.Handler.Handle(Command(h.PayeeId, chosen.Id), default);

        result.IsSuccess.Should().BeTrue();
        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.SelectedPlanAssignmentId.Should().Be(chosen.Id);
    }

    // (c) One plan → no ambiguity, no friction: unchanged behaviour.
    [Fact]
    public async Task Payee_on_one_plan_succeeds_without_selecting_anything()
    {
        var h = BuildHarness(nameof(Payee_on_one_plan_succeeds_without_selecting_anything), planCount: 1);

        var result = await h.Handler.Handle(Command(h.PayeeId, selectedPlanAssignmentId: null), default);

        result.IsSuccess.Should().BeTrue();
        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.SelectedPlanAssignmentId.Should().BeNull();
    }

    // A payee with no applicable plan keeps working exactly as before (tx lands and stays Pending).
    [Fact]
    public async Task Payee_on_no_plan_succeeds_without_selecting_anything()
    {
        var h = BuildHarness(nameof(Payee_on_no_plan_succeeds_without_selecting_anything), planCount: 0);

        var result = await h.Handler.Handle(Command(h.PayeeId, selectedPlanAssignmentId: null), default);

        result.IsSuccess.Should().BeTrue();
    }

    // A client cannot smuggle in an assignment that is not a real candidate.
    [Fact]
    public async Task A_selection_that_is_not_a_candidate_is_rejected()
    {
        var h = BuildHarness(nameof(A_selection_that_is_not_a_candidate_is_rejected), planCount: 2);

        var result = await h.Handler.Handle(Command(h.PayeeId, Guid.NewGuid()), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not applicable");
        (await h.Db.CompensationTransactions.AnyAsync()).Should().BeFalse();
    }

    // Guard: with no payee there is no plan to attribute to.
    [Fact]
    public async Task A_selection_without_a_payee_is_rejected()
    {
        var h = BuildHarness(nameof(A_selection_without_a_payee_is_rejected), planCount: 2);

        var command = Command(h.PayeeId, h.Assignments[0].Id) with { PayeeId = null };
        var result = await h.Handler.Handle(command, default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("no payee");
    }
}
