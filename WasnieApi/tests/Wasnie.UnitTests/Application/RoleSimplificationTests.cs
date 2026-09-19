using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Users.Commands;
using Wasnie.Application.Features.Users.Handlers;
using Wasnie.Application.Features.Users.Queries;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Exceptions;
using Wasnie.Domain.Identity;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Role simplification (KAN-92/KAN-99): two roles may be granted today — TenantAdmin and Rep.
/// CompManager and Manager are HIDDEN, not deleted.
///
/// ★★ THE INVITATION IS THE OTHER DOOR. <see cref="UserAccessInvariantTests"/> covers changing a role;
/// this covers creating somebody in one, which is the path a hidden picker most tempts a script to use.
/// Both refusals happen before the handler touches the database, so the substitutes are never reached.
/// </summary>
public sealed class RoleSimplificationTests
{
    private static InviteUserHandler InviteHandler()
    {
        var auth = Substitute.For<IAuthorizationService>();
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        return new InviteUserHandler(
            Substitute.For<IApplicationDbContext>(),
            auth,
            Substitute.For<ITenantContext>(),
            Substitute.For<ICurrentUserService>(),
            Substitute.For<IIdentityService>(),
            Substitute.For<ITierLimitChecker>(),
            Substitute.For<IEmailService>(),
            Substitute.For<IAuditService>(),
            Options.Create(new ResendOptions()),
            Substitute.For<IClock>(),
            Substitute.For<IGuidGenerator>(),
            NullLogger<InviteUserHandler>.Instance);
    }

    [Theory]
    [InlineData(Roles.CompManager)]
    [InlineData(Roles.Manager)]
    [InlineData(" manager ")]
    public async Task Inviting_somebody_into_a_hidden_role_is_refused(string hidden)
    {
        var ex = await Assert.ThrowsAsync<DomainCodedException>(() =>
            InviteHandler().Handle(new InviteUserCommand("new@acme.com", hidden), default));

        ex.Code.Should().Be(InvitationRefusal.RoleNotAssignable);
    }

    [Fact]
    public async Task Inviting_into_a_role_that_does_not_exist_keeps_its_own_code()
    {
        var ex = await Assert.ThrowsAsync<DomainCodedException>(() =>
            InviteHandler().Handle(new InviteUserCommand("new@acme.com", "Auditor"), default));

        ex.Code.Should().Be(InvitationRefusal.RoleUnknown);
    }

    /// <summary>
    /// ★★ THE ROLES ENDPOINT IS WHERE THE PICKERS GET THEIR OPTIONS. It must still describe every role
    /// — the access panel has to explain somebody who already holds a hidden one — and flag which of
    /// them may be granted, so the screens never keep a list of their own.
    /// </summary>
    [Fact]
    public async Task The_roles_endpoint_describes_every_role_and_flags_the_assignable_ones()
    {
        var auth = Substitute.For<IAuthorizationService>();
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await new ListRolePermissionsHandler(auth).Handle(new ListRolePermissionsQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(r => r.Role).Should().Equal(
            Roles.TenantAdmin, Roles.CompManager, Roles.Manager, Roles.Rep);
        result.Value!.Where(r => r.Assignable).Select(r => r.Role).Should().Equal(Roles.TenantAdmin, Roles.Rep);
        result.Value!.Single(r => r.Role == Roles.CompManager).Permissions.Should().NotBeEmpty();
    }
}
