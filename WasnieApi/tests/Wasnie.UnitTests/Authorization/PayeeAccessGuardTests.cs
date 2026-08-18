using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Authorization;

/// <summary>
/// The BOLA/IDOR guard. Every test here is a security test: each one fails if the corresponding branch
/// of <see cref="PayeeAccessGuard"/> is deleted, which is the only reason to trust it.
///
/// The bug being fenced in: Ledger.Read is held by EVERY role (a rep seeing why their own pay shrank is
/// the product), so before this class the permission check alone authorised reading ANY payee's ledger
/// by substituting an id.
/// </summary>
public sealed class PayeeAccessGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private const string RepUser = "user-rep";
    private const string ManagerUser = "user-manager";

    private sealed record Harness(
        ApplicationDbContext Db,
        Guid TenantId,
        Guid OwnPayeeId,
        Guid ForeignPayeeId,
        Guid ReportPayeeId,
        Guid UnlinkedPayeeId);

    /// <summary>
    /// Four payees in one tenant: the rep's own (linked to RepUser), a stranger's, a direct report of
    /// the manager, and one linked to nobody.
    /// </summary>
    private static Harness Seed(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        Payee New(string name, string code, string? userId)
        {
            var p = Payee.Create(tenantId, name, code, $"{code}@acme.com".ToLowerInvariant(),
                new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
            if (userId is not null) p.LinkToUser(userId, "test", Now);
            db.Payees.Add(p);
            return p;
        }

        var own = New("Ana Rep", "EMP-REP", RepUser);
        var foreign = New("Bruno Stranger", "EMP-FOREIGN", "user-someone-else");
        var manager = New("Carla Manager", "EMP-MGR", ManagerUser);
        var report = New("Dario Report", "EMP-REPORT", "user-report");
        var unlinked = New("Eva Contractor", "EMP-UNLINKED", null);

        db.SaveChanges();

        report.AssignManager(manager.Id, "test", Now);
        db.SaveChanges();

        return new Harness(db, tenantId, own.Id, foreign.Id, report.Id, unlinked.Id);
    }

    private static PayeeAccessGuard Guard(Harness h, string? role, string? userId)
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(userId);

        var claims = Substitute.For<IClaimsService>();
        claims.GetRole().Returns(role);

        return new PayeeAccessGuard(h.Db, currentUser, claims);
    }

    // ── Rep ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_rep_cannot_read_another_payee()
    {
        var h = Seed(nameof(A_rep_cannot_read_another_payee));

        (await Guard(h, "Rep", RepUser).CanReadAsync(h.ForeignPayeeId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_rep_can_read_their_own_payee()
    {
        var h = Seed(nameof(A_rep_can_read_their_own_payee));

        (await Guard(h, "Rep", RepUser).CanReadAsync(h.OwnPayeeId)).Should().BeTrue();
    }

    [Fact]
    public async Task An_unlinked_payee_is_readable_by_nobody_except_the_supervisory_roles()
    {
        var h = Seed(nameof(An_unlinked_payee_is_readable_by_nobody_except_the_supervisory_roles));

        (await Guard(h, "Rep", RepUser).CanReadAsync(h.UnlinkedPayeeId)).Should().BeFalse();
        (await Guard(h, "Manager", ManagerUser).CanReadAsync(h.UnlinkedPayeeId)).Should().BeFalse();
        (await Guard(h, "TenantAdmin", "user-admin").CanReadAsync(h.UnlinkedPayeeId)).Should().BeTrue();
    }

    /// <summary>
    /// ★ The state EVERY payee is in immediately after the migration: no UserId anywhere. A rep in that
    /// tenant must see NOTHING rather than everything — the difference between fail-closed and the
    /// vulnerability this WI closed.
    /// </summary>
    [Fact]
    public async Task A_rep_whose_own_payee_is_not_linked_sees_nothing()
    {
        var h = Seed(nameof(A_rep_whose_own_payee_is_not_linked_sees_nothing));

        var visibility = await Guard(h, "Rep", "user-with-no-payee").GetVisibilityAsync();

        visibility.All.Should().BeFalse();
        visibility.PayeeIds.Should().BeEmpty();
        (await Guard(h, "Rep", "user-with-no-payee").CanReadAsync(h.OwnPayeeId)).Should().BeFalse();
    }

    // ── Manager ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_manager_can_read_their_own_payee_and_their_direct_reports()
    {
        var h = Seed(nameof(A_manager_can_read_their_own_payee_and_their_direct_reports));
        var guard = Guard(h, "Manager", ManagerUser);

        (await guard.CanReadAsync(h.ReportPayeeId)).Should().BeTrue();
    }

    [Fact]
    public async Task A_manager_cannot_read_a_payee_who_does_not_report_to_them()
    {
        var h = Seed(nameof(A_manager_cannot_read_a_payee_who_does_not_report_to_them));

        (await Guard(h, "Manager", ManagerUser).CanReadAsync(h.ForeignPayeeId)).Should().BeFalse();
    }

    // ── Supervisory roles ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("TenantAdmin")]
    [InlineData("CompManager")]
    public async Task Supervisory_roles_read_every_payee(string role)
    {
        var h = Seed($"{nameof(Supervisory_roles_read_every_payee)}-{role}");
        var guard = Guard(h, role, "user-admin");

        (await guard.GetVisibilityAsync()).All.Should().BeTrue();
        (await guard.CanReadAsync(h.ForeignPayeeId)).Should().BeTrue();
    }

    // ── Fail-closed on everything unresolved ──────────────────────────────────

    [Theory]
    [InlineData(null, RepUser)]          // no role claim
    [InlineData("", RepUser)]            // empty role claim
    [InlineData("SomeFutureRole", RepUser)] // a role nobody taught this class about
    [InlineData("Rep", null)]            // authenticated principal with no user id
    [InlineData("Rep", "")]              // ditto, empty
    public async Task Anything_unresolved_denies(string? role, string? userId)
    {
        var h = Seed($"{nameof(Anything_unresolved_denies)}-{role}-{userId}");
        var guard = Guard(h, role, userId);

        (await guard.GetVisibilityAsync()).PayeeIds.Should().BeEmpty();
        (await guard.CanReadAsync(h.OwnPayeeId)).Should().BeFalse();
        (await guard.CanReadAsync(h.ForeignPayeeId)).Should().BeFalse();
    }

    /// <summary>
    /// The visibility is resolved once per request. Two handlers in one request must agree, and must
    /// not pay for the lookup twice.
    /// </summary>
    [Fact]
    public async Task Visibility_is_resolved_once_per_request()
    {
        var h = Seed(nameof(Visibility_is_resolved_once_per_request));
        var guard = Guard(h, "Rep", RepUser);

        var first = await guard.GetVisibilityAsync();
        var second = await guard.GetVisibilityAsync();

        second.Should().BeSameAs(first);
    }
}
