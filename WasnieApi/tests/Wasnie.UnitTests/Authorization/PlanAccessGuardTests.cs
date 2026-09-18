using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Authorization;

/// <summary>
/// KAN-93 bug 6. The guard that lets a rep read THEIR plan without opening the catalogue.
///
/// ★★ EVERY TEST HERE IS A SECURITY TEST. `Plans.ReadOwn` exists so somebody can check the arithmetic
/// on their own commission; the whole value of narrowing it instead of granting `Plans.Read` lives in
/// this class, and each test fails if the corresponding branch is deleted. That is the only reason to
/// trust it.
/// </summary>
public sealed class PlanAccessGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateRange Year = DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

    private const string RepUser = "user-rep";

    private sealed record Harness(
        ApplicationDbContext Db, Guid TenantId, Guid MyPlanId, Guid ForeignPlanId, Guid MyPayeeId);

    /// <summary>
    /// Two plans in one workspace: one the rep is assigned to, one belonging to somebody else.
    /// </summary>
    private static Harness Seed(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        Plan NewPlan(string name)
        {
            var plan = Plan.Create(tenantId, name, "test", Year, "EUR", "test", Guid.NewGuid(), Now, Guid.NewGuid());
            db.CompensationPlans.Add(plan);
            return plan;
        }

        Payee NewPayee(string name, string code, string? userId)
        {
            var payee = Payee.Create(tenantId, name, code, $"{code}@acme.com".ToLowerInvariant(),
                new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
            if (userId is not null) payee.LinkToUser(userId, "test", Now);
            db.Payees.Add(payee);
            return payee;
        }

        var mine = NewPlan("My plan");
        var foreign = NewPlan("Somebody else's plan");
        var myPayee = NewPayee("Ana Garcia", "EMP-REP", RepUser);
        var stranger = NewPayee("Bruno Silva", "EMP-OTHER", "user-stranger");
        db.SaveChanges();

        db.PlanAssignments.Add(PlanAssignment.Create(
            tenantId, mine.Id, myPayee.Id,
            PayeeReference.Snapshot(myPayee.Id, "Ana Garcia", "EMP-REP"),
            Year, "test", Guid.NewGuid(), Now, Guid.NewGuid()));

        db.PlanAssignments.Add(PlanAssignment.Create(
            tenantId, foreign.Id, stranger.Id,
            PayeeReference.Snapshot(stranger.Id, "Bruno Silva", "EMP-OTHER"),
            Year, "test", Guid.NewGuid(), Now, Guid.NewGuid()));

        db.SaveChanges();

        return new Harness(db, tenantId, mine.Id, foreign.Id, myPayee.Id);
    }

    private static PlanAccessGuard GuardFor(Harness h, string? userId, params string[] permissions)
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(userId);

        var auth = Substitute.For<IAuthorizationService>();
        auth.HasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(permissions.Contains(ci.Arg<string>())));

        return new PlanAccessGuard(h.Db, currentUser, auth);
    }

    /// <summary>★★ The case the ticket is about: the rep's own plan opens.</summary>
    [Fact]
    public async Task A_rep_may_read_a_plan_they_are_assigned_to()
    {
        var h = Seed(nameof(A_rep_may_read_a_plan_they_are_assigned_to));
        var guard = GuardFor(h, RepUser, Permission.PlansReadOwn);

        (await guard.CanReadAsync(h.MyPlanId)).Should().BeTrue();
    }

    /// <summary>
    /// ★★ AND THE REST OF THE CATALOGUE DOES NOT. This is the whole reason `Plans.ReadOwn` exists as a
    /// separate permission: granting `Plans.Read` would have made this true, and a rep could then read
    /// how every other team in the company is paid.
    /// </summary>
    [Fact]
    public async Task A_rep_may_not_read_a_plan_they_are_not_assigned_to()
    {
        var h = Seed(nameof(A_rep_may_not_read_a_plan_they_are_not_assigned_to));
        var guard = GuardFor(h, RepUser, Permission.PlansReadOwn);

        (await guard.CanReadAsync(h.ForeignPlanId)).Should().BeFalse();
    }

    /// <summary>
    /// ★★ A DEACTIVATED ASSIGNMENT STILL OPENS THE PLAN, ON PURPOSE. They were PAID under it, the
    /// payment is still in their ledger, and the rules behind money somebody already received have to
    /// stay checkable. Filtering on Active status here would mean unassigning somebody silently
    /// deletes their ability to verify their own history — the opposite of what this feature is for.
    /// </summary>
    [Fact]
    public async Task A_deactivated_assignment_still_opens_the_plan_behind_past_pay()
    {
        var h = Seed(nameof(A_deactivated_assignment_still_opens_the_plan_behind_past_pay));

        var assignment = h.Db.PlanAssignments.Single(a => a.PlanId == h.MyPlanId);
        assignment.Deactivate("test", Now, Guid.NewGuid());
        await h.Db.SaveChangesAsync();
        assignment.Status.Should().NotBe(AssignmentStatus.Active, "otherwise this test proves nothing");

        var guard = GuardFor(h, RepUser, Permission.PlansReadOwn);

        (await guard.CanReadAsync(h.MyPlanId)).Should().BeTrue();
    }

    /// <summary>
    /// ★★ AN UNLINKED ACCOUNT RESOLVES TO NOTHING, NOT TO EVERYTHING. The query is anchored on the
    /// reader's own payee; an identity that cannot be resolved must produce an empty set rather than a
    /// wildcard. This is the single most important property of the guard.
    /// </summary>
    [Fact]
    public async Task An_account_linked_to_no_payee_may_read_nothing()
    {
        var h = Seed(nameof(An_account_linked_to_no_payee_may_read_nothing));
        var guard = GuardFor(h, "user-with-no-payee", Permission.PlansReadOwn);

        (await guard.CanReadAsync(h.MyPlanId)).Should().BeFalse();
        (await guard.CanReadAsync(h.ForeignPlanId)).Should().BeFalse();
    }

    [Fact]
    public async Task An_anonymous_principal_may_read_nothing()
    {
        var h = Seed(nameof(An_anonymous_principal_may_read_nothing));
        var guard = GuardFor(h, null, Permission.PlansReadOwn);

        (await guard.CanReadAsync(h.MyPlanId)).Should().BeFalse();
    }

    /// <summary>★ Neither permission is no access, whatever the assignments say.</summary>
    [Fact]
    public async Task Somebody_holding_neither_permission_may_read_nothing()
    {
        var h = Seed(nameof(Somebody_holding_neither_permission_may_read_nothing));
        var guard = GuardFor(h, RepUser);

        (await guard.CanReadAsync(h.MyPlanId)).Should().BeFalse();
    }

    /// <summary>
    /// ★★ AN ADMINISTRATOR IS UNCHANGED. Running compensation IS reading the catalogue; a guard that
    /// narrowed `Plans.Read` would not close a hole, it would break the product. Note this holder is
    /// linked to no payee at all and still reads everything.
    /// </summary>
    [Fact]
    public async Task A_plans_read_holder_reads_every_plan()
    {
        var h = Seed(nameof(A_plans_read_holder_reads_every_plan));
        var guard = GuardFor(h, "user-admin", Permission.PlansRead);

        (await guard.CanReadAsync(h.MyPlanId)).Should().BeTrue();
        (await guard.CanReadAsync(h.ForeignPlanId)).Should().BeTrue();
    }
}
