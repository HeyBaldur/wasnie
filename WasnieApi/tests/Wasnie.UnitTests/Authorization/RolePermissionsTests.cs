using FluentAssertions;
using Wasnie.Application.Authorization;
using Wasnie.Domain.Authorization;

namespace Wasnie.UnitTests.Authorization;

public sealed class RolePermissionsTests
{
    [Theory]
    [InlineData("TenantAdmin", Permission.PayeesCreate)]
    [InlineData("TenantAdmin", Permission.PlansCreate)]
    [InlineData("TenantAdmin", Permission.QuotasSet)]
    [InlineData("TenantAdmin", Permission.AssignmentsCreate)]
    [InlineData("TenantAdmin", Permission.SubscriptionManage)]
    [InlineData("TenantAdmin", Permission.TransactionsCreate)]
    [InlineData("TenantAdmin", Permission.TransactionsRead)]
    [InlineData("TenantAdmin", Permission.TransactionsUpdate)]
    [InlineData("TenantAdmin", Permission.PayeesDeactivate)]
    [InlineData("CompManager", Permission.PayeesCreate)]
    [InlineData("CompManager", Permission.PlansCreate)]
    [InlineData("CompManager", Permission.QuotasSet)]
    [InlineData("CompManager", Permission.AssignmentsCreate)]
    [InlineData("CompManager", Permission.ReportsViewAll)]
    [InlineData("CompManager", Permission.TransactionsCreate)]
    [InlineData("CompManager", Permission.TransactionsRead)]
    [InlineData("CompManager", Permission.TransactionsUpdate)]
    [InlineData("CompManager", Permission.PayeesDeactivate)]
    [InlineData("Manager", Permission.PayeesRead)]
    [InlineData("Manager", Permission.QuotasRead)]
    [InlineData("Rep", Permission.AssignmentsRead)]
    // KAN-93. The Rep keeps the pair that feeds their own dashboard, and losing Payees.Read must not
    // touch them — these two are what make the removal safe rather than merely smaller.
    [InlineData("Rep", Permission.LedgerRead)]
    [InlineData("Rep", Permission.LedgerSummaryRead)]
    // KAN-93 bug 6. The rules of the plans they are ON — so they can check the arithmetic on their
    // own commission instead of keeping a spreadsheet nobody reconciles.
    [InlineData("Rep", Permission.PlansReadOwn)]
    public void HasPermission_ReturnsTrue_ForGrantedPermissions(string role, string permission)
    {
        RolePermissions.HasPermission(role, permission).Should().BeTrue();
    }

    [Theory]
    [InlineData("Manager", Permission.PayeesCreate)]
    [InlineData("Manager", Permission.PlansCreate)]
    [InlineData("Manager", Permission.SubscriptionManage)]
    [InlineData("Manager", Permission.TransactionsCreate)]
    [InlineData("Manager", Permission.TransactionsUpdate)]
    [InlineData("Manager", Permission.PayeesDeactivate)]
    [InlineData("Rep", Permission.PayeesCreate)]
    [InlineData("Rep", Permission.PlansCreate)]
    [InlineData("Rep", Permission.QuotasSet)]
    [InlineData("Rep", Permission.TransactionsCreate)]
    [InlineData("Rep", Permission.TransactionsUpdate)]
    [InlineData("Rep", Permission.PayeesDeactivate)]
    // KAN-93. THE REP HAS NO Payees.Read AT ALL, and this line is the whole of Bug 1. The rail entry,
    // the /payees route guard and ListPayeesHandler all ask this one key, so its absence closes the
    // menu, the URL and the endpoint together. If somebody puts it back to "let a rep see their own
    // record", their own record is what /api/me/dashboard is for.
    [InlineData("Rep", Permission.PayeesRead)]
    // KAN-93 bug 6. THE CATALOGUE STAYS SHUT. `Plans.ReadOwn` is not a stepping stone to this one:
    // `Plans.Read` opens every plan of every team, the version history, the simulator and the
    // multi-plan payee lookup. If somebody ever "simplifies" the pair into one, this line fails.
    [InlineData("Rep", Permission.PlansRead)]
    [InlineData("CompManager", Permission.SubscriptionManage)]
    public void HasPermission_ReturnsFalse_ForDeniedPermissions(string role, string permission)
    {
        RolePermissions.HasPermission(role, permission).Should().BeFalse();
    }

    /// <summary>
    /// KAN-93. Manager keeps it; only the Rep lost it.
    ///
    /// ★ ASSERTED AS A PAIR, because the risk in this change is over-reach. Explaining a reduced
    /// payment to a rep is a manager's job and PayeeAccessGuard already narrows them to their direct
    /// reports — widening the removal to Manager would be a different decision with a different owner.
    /// </summary>
    [Fact]
    public void Only_the_rep_lost_payee_read()
    {
        RolePermissions.HasPermission("Manager", Permission.PayeesRead).Should().BeTrue();
        RolePermissions.HasPermission("CompManager", Permission.PayeesRead).Should().BeTrue();
        RolePermissions.HasPermission("TenantAdmin", Permission.PayeesRead).Should().BeTrue();
        RolePermissions.HasPermission("Rep", Permission.PayeesRead).Should().BeFalse();
    }

    /// <summary>
    /// KAN-93 bug 6. Only the Rep got the narrow plan-reading permission.
    ///
    /// ★ ASSERTED AS A SET, because the risk with a new permission is that it spreads. The
    /// administrator roles do not need it — they hold `Plans.Read`, which the guard already treats as
    /// unrestricted — and a Manager wanting the same view is a separate decision with its own owner.
    /// </summary>
    [Fact]
    public void Only_the_rep_holds_the_narrow_plan_permission()
    {
        RolePermissions.HasPermission("Rep", Permission.PlansReadOwn).Should().BeTrue();
        RolePermissions.HasPermission("Manager", Permission.PlansReadOwn).Should().BeFalse();
        RolePermissions.HasPermission("CompManager", Permission.PlansReadOwn).Should().BeFalse();
        RolePermissions.HasPermission("TenantAdmin", Permission.PlansReadOwn).Should().BeFalse();
    }

    /// <summary>
    /// KAN-93 bug 6, follow-up: the rep must be able to READ a rule, which is not the same as editing
    /// one.
    ///
    /// ★★ THE ROUTE `plans/:id/rules/:ruleId` IS BOTH THE EDITOR AND THE READER — "Edit" on a Draft,
    /// "View" on an Active plan, one URL, the component switching itself to read-only from the plan's
    /// status. Gating it on `Plans.Read` sent every rep to Access Denied on the one screen this whole
    /// feature exists to show them, which is exactly what was reported.
    ///
    /// ★ AND THE FIELD CATALOGUE HAS TO OPEN WITH IT. A trigger condition renders its field through
    /// `GetTriggerFields`; without it the rep would read a condition with a BLANK field name — a page
    /// quietly lying about how they are paid, which is worse than the refusal it replaced. Those are
    /// the engine's own field names, identical for every tenant, so widening them discloses nothing.
    /// The tenant's real CATEGORY VALUES are deliberately not widened alongside them.
    /// </summary>
    [Fact]
    public void The_rep_can_read_a_rule_without_being_able_to_administer_plans()
    {
        // What the read-only rule screen needs.
        RolePermissions.HasPermission("Rep", Permission.PlansReadOwn).Should().BeTrue();

        // And what it still must not have: the catalogue, the editor's write verbs, the brake.
        RolePermissions.HasPermission("Rep", Permission.PlansRead).Should().BeFalse();
        RolePermissions.HasPermission("Rep", Permission.PlansUpdate).Should().BeFalse();
        RolePermissions.HasPermission("Rep", Permission.PlansCreate).Should().BeFalse();
        RolePermissions.HasPermission("Rep", Permission.PlansArchive).Should().BeFalse();
        RolePermissions.HasPermission("Rep", Permission.PlansStopRule).Should().BeFalse();
    }

    [Fact]
    public void HasPermission_ReturnsFalse_ForUnknownRole()
    {
        RolePermissions.HasPermission("NonExistentRole", Permission.PayeesRead).Should().BeFalse();
    }

    [Fact]
    public void GetPermissions_ReturnsAllPermissions_ForTenantAdmin()
    {
        var perms = RolePermissions.GetPermissions("TenantAdmin");
        perms.Should().Contain(Permission.SubscriptionManage);
        perms.Should().Contain(Permission.PayeesCreate);
        perms.Should().Contain(Permission.PlansActivate);
        perms.Should().Contain(Permission.AssignmentsCreate);
    }

    [Fact]
    public void GetPermissions_ReturnsEmptySet_ForUnknownRole()
    {
        var perms = RolePermissions.GetPermissions("Ghost");
        perms.Should().BeEmpty();
    }

    [Fact]
    public void TenantAdmin_HasMorePermissions_ThanCompManager()
    {
        var adminPerms = RolePermissions.GetPermissions("TenantAdmin");
        var managerPerms = RolePermissions.GetPermissions("CompManager");
        adminPerms.Should().Contain(Permission.SubscriptionManage);
        managerPerms.Should().NotContain(Permission.SubscriptionManage);
    }
}
