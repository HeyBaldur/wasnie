using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using MediatR;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Handlers.Plans;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Who may simulate a rule — the real guard, not a double.
///
/// ★★ THE DEFECT THIS CLOSES. KAN-93 narrowed the Rep to <c>Plans.ReadOwn</c> so they could read the
/// rules of their own plan, and deliberately left the simulator on <c>Plans.Read</c>. Nothing told the
/// screen: the panel kept rendering, the amount input kept accepting keystrokes, and each one produced
/// a 403 — seven in the reported case — plus a PermissionDenied audit row apiece. Offering a control
/// the server refuses is the rule §5.8 forbids, and the two ways out were to hide the panel or to
/// admit the reader. Admitting them is what the rule screen is for: simulating a rule you are paid
/// under reads no data and touches nobody.
///
/// ★★ THESE TESTS DRIVE THE HANDLER THROUGH THE REAL <see cref="PlanAccessGuard"/>. Asserting on the
/// guard alone would pass just as happily with the handler never calling it — which is precisely the
/// state the product was in.
/// </summary>
public sealed class SimulateRulePermissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateRange Year = DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

    private const string MyUser = "user-mine";

    private sealed record Harness(ApplicationDbContext Db, Guid TenantId, Guid MyPlanId, Guid ForeignPlanId);

    private static Harness Seed(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<IPublisher>());

        var mine = Plan.Create(tenantId, "My plan", "", Year, "EUR", "test", Guid.NewGuid(), Now, Guid.NewGuid());
        var foreign = Plan.Create(tenantId, "Somebody else's", "", Year, "EUR", "test", Guid.NewGuid(), Now, Guid.NewGuid());
        db.CompensationPlans.AddRange(mine, foreign);

        var payee = Payee.Create(tenantId, "Ana Garcia", "EMP-MINE", "ana@acme.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        payee.LinkToUser(MyUser, "test", Now);
        db.Payees.Add(payee);

        db.PlanAssignments.Add(PlanAssignment.Create(
            tenantId, mine.Id, payee.Id,
            PayeeReference.Snapshot(payee.Id, "Ana Garcia", "EMP-MINE"),
            Year, "test", Guid.NewGuid(), Now, Guid.NewGuid()));

        db.SaveChanges();

        return new Harness(db, tenantId, mine.Id, foreign.Id);
    }

    /// <summary>The handler wired to the REAL guard, for a caller holding exactly these permissions.</summary>
    private static SimulateRuleHandler HandlerFor(
        Harness h, string? userId, bool sandbox, params string[] permissions)
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(userId);

        var auth = Substitute.For<IAuthorizationService>();
        auth.HasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(permissions.Contains(ci.Arg<string>())));
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => permissions.Contains(ci.Arg<string>())
                ? Task.CompletedTask
                : Task.FromException(new ForbiddenException($"Missing {ci.Arg<string>()}.")));

        var sandboxScope = Substitute.For<ISandboxScope>();
        sandboxScope.IsSandbox.Returns(sandbox);

        var clock = Substitute.For<IClock>();
        clock.UtcNowOffset.Returns(Now);

        var guid = Substitute.For<IGuidGenerator>();
        guid.NewGuid().Returns(_ => Guid.NewGuid());

        return new SimulateRuleHandler(
            h.Db, auth,
            new PlanAccessGuard(h.Db, currentUser, auth, sandboxScope),
            new RuleCalculationExplainer(NullLogger<RuleCalculationExplainer>.Instance),
            guid, clock);
    }

    /// <summary>A flat 10% rule over the given plan — the simplest definition that produces a figure.</summary>
    private static SimulateRuleQuery Query(Guid planId) => new(
        PlanId: planId,
        Name: "Simulated rule",
        Measurement: new Measurement { Type = MeasurementType.Revenue },
        RateTable: RateTable.Flat(0.10m),
        Trigger: null,
        Modifier: null,
        Cap: null,
        Floor: null,
        Amount: 5000m,
        Quantity: 1);

    /// <summary>
    /// ★★ THE REPORTED CASE, FROM THE OTHER SIDE. A rep opens the rule of a plan they are assigned to,
    /// types an amount, and gets a figure instead of a 403.
    /// </summary>
    [Fact]
    public async Task A_rep_can_simulate_a_rule_of_their_own_plan()
    {
        var h = Seed(nameof(A_rep_can_simulate_a_rule_of_their_own_plan));
        var handler = HandlerFor(h, MyUser, sandbox: false, Permission.PlansReadOwn);

        var result = await handler.Handle(Query(h.MyPlanId), default);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// ★★ AND ONLY THEIR OWN. Plans.ReadOwn is not a quiet Plans.Read: a rep aiming the simulator at a
    /// plan they were never assigned to is asking about somebody else's compensation design, and the
    /// answer would leak its rates a definition at a time.
    /// </summary>
    [Fact]
    public async Task A_rep_cannot_simulate_a_rule_of_a_plan_that_is_not_theirs()
    {
        var h = Seed(nameof(A_rep_cannot_simulate_a_rule_of_a_plan_that_is_not_theirs));
        var handler = HandlerFor(h, MyUser, sandbox: false, Permission.PlansReadOwn);

        var act = () => handler.Handle(Query(h.ForeignPlanId), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    /// <summary>
    /// ★★ AND THE REFUSAL GOES THROUGH RequireAsync, WHICH IS WHAT WRITES THE AUDIT ROW (§B1). The guard
    /// on its own would refuse in silence, and a 403 that appears in no log is the failure mode this
    /// codebase has a rule about — it cost an hour of debugging the first time it happened.
    /// </summary>
    [Fact]
    public async Task A_refused_simulation_asks_the_permission_service_so_the_denial_is_recorded()
    {
        var h = Seed(nameof(A_refused_simulation_asks_the_permission_service_so_the_denial_is_recorded));

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(MyUser);

        var auth = Substitute.For<IAuthorizationService>();
        auth.HasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.Arg<string>() == Permission.PlansReadOwn));
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ForbiddenException("nope")));

        var sandboxScope = Substitute.For<ISandboxScope>();
        sandboxScope.IsSandbox.Returns(false);

        var clock = Substitute.For<IClock>();
        clock.UtcNowOffset.Returns(Now);

        var handler = new SimulateRuleHandler(
            h.Db, auth,
            new PlanAccessGuard(h.Db, currentUser, auth, sandboxScope),
            new RuleCalculationExplainer(NullLogger<RuleCalculationExplainer>.Instance),
            Substitute.For<IGuidGenerator>(), clock);

        try { await handler.Handle(Query(h.ForeignPlanId), default); } catch (ForbiddenException) { }

        await auth.Received(1).RequireAsync(Permission.PlansRead, Arg.Any<CancellationToken>());
    }

    /// <summary>★ An administrator is untouched: Plans.Read still simulates anything in the catalogue.</summary>
    [Fact]
    public async Task An_administrator_can_still_simulate_any_plan()
    {
        var h = Seed(nameof(An_administrator_can_still_simulate_any_plan));
        var handler = HandlerFor(h, "admin-user", sandbox: false, Permission.PlansRead);

        var result = await handler.Handle(Query(h.ForeignPlanId), default);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// ★★ THE GUIDED TOUR HAD THE SAME DEAD END, ON THE SCREEN WHOSE JOB IS TEACHING THE PRODUCT. Its
    /// simulator posts to the sandbox route, which reaches this same handler; a rep practising there had
    /// no assignment to resolve against, because the tour never links a payee to a login. The practice
    /// schema is the caller's own by construction, so the guard admits it.
    /// </summary>
    [Fact]
    public async Task Inside_the_sandbox_a_rep_can_simulate_without_an_assignment()
    {
        var h = Seed(nameof(Inside_the_sandbox_a_rep_can_simulate_without_an_assignment));
        var handler = HandlerFor(h, MyUser, sandbox: true, Permission.PlansReadOwn);

        var result = await handler.Handle(Query(h.ForeignPlanId), default);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// ★ AND THE SANDBOX FLAG IS THE ONLY THING THAT WIDENS IT. Set by EnterSandboxFilter and by nothing
    /// else, so an ordinary request is decided by the two permission rules exactly as before — this pins
    /// that the short-circuit did not quietly become "everybody may simulate everything".
    /// </summary>
    [Fact]
    public async Task Outside_the_sandbox_a_caller_with_no_plan_permission_is_refused()
    {
        var h = Seed(nameof(Outside_the_sandbox_a_caller_with_no_plan_permission_is_refused));
        var handler = HandlerFor(h, MyUser, sandbox: false);

        var act = () => handler.Handle(Query(h.MyPlanId), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
