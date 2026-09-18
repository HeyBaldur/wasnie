using FluentAssertions;
using Wasnie.Application.Authorization;
using Wasnie.Domain.Authorization;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// What the Rep role actually holds, asserted against the source.
///
/// ★★ IT EXISTS BECAUSE A RUNNING API DISAGREED WITH THE SOURCE. The access panel reported the Rep
/// holding Payees.Read and not Plans.ReadOwn — the pre-KAN-93 list — while RolePermissions.cs said the
/// opposite. A static readonly set is built when the type is first loaded, and `dotnet watch` applies
/// method deltas without re-running a static initialiser, so the process kept serving the old list
/// from memory while every new endpoint around it was the new code. Nothing was wrong with the
/// permission map; the binary was half old, which is the worst kind of half.
/// </summary>
public sealed class RolePermissionsSourceTests
{
    [Fact]
    public void A_rep_does_not_hold_payees_read()
    {
        RolePermissions.GetPermissions("Rep").Should().NotContain(Permission.PayeesRead);
    }

    [Fact]
    public void A_rep_holds_plans_read_own()
    {
        RolePermissions.GetPermissions("Rep").Should().Contain(Permission.PlansReadOwn);
    }

    /// <summary>★ And the administrator reads every plan, which is what makes Plans.ReadOwn redundant
    /// for them rather than missing — see the access map's PLANS_READ_OWN line.</summary>
    [Fact]
    public void An_administrator_holds_plans_read()
    {
        RolePermissions.GetPermissions("TenantAdmin").Should().Contain(Permission.PlansRead);
    }
}
