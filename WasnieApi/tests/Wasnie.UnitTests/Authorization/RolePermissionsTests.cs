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
    [InlineData("CompManager", Permission.PayeesCreate)]
    [InlineData("CompManager", Permission.PlansCreate)]
    [InlineData("CompManager", Permission.QuotasSet)]
    [InlineData("CompManager", Permission.AssignmentsCreate)]
    [InlineData("CompManager", Permission.ReportsViewAll)]
    [InlineData("CompManager", Permission.TransactionsCreate)]
    [InlineData("CompManager", Permission.TransactionsRead)]
    [InlineData("Manager", Permission.PayeesRead)]
    [InlineData("Manager", Permission.QuotasRead)]
    [InlineData("Rep", Permission.PayeesRead)]
    [InlineData("Rep", Permission.AssignmentsRead)]
    public void HasPermission_ReturnsTrue_ForGrantedPermissions(string role, string permission)
    {
        RolePermissions.HasPermission(role, permission).Should().BeTrue();
    }

    [Theory]
    [InlineData("Manager", Permission.PayeesCreate)]
    [InlineData("Manager", Permission.PlansCreate)]
    [InlineData("Manager", Permission.SubscriptionManage)]
    [InlineData("Manager", Permission.TransactionsCreate)]
    [InlineData("Rep", Permission.PayeesCreate)]
    [InlineData("Rep", Permission.PlansCreate)]
    [InlineData("Rep", Permission.QuotasSet)]
    [InlineData("Rep", Permission.TransactionsCreate)]
    [InlineData("CompManager", Permission.SubscriptionManage)]
    public void HasPermission_ReturnsFalse_ForDeniedPermissions(string role, string permission)
    {
        RolePermissions.HasPermission(role, permission).Should().BeFalse();
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
