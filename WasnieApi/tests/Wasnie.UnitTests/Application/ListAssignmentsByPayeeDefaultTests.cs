using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Handlers.Assignments;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.Builders;
using Wasnie.UnitTests.TestDoubles;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The safe-default contract for "a payee's assignments". Callers that say nothing must get only
/// ACTIVE assignments — the previous default returned every status, which is why a payee's profile
/// card counted deactivated assignments as current. Seeing deactivated rows now has to be asked for.
/// </summary>
public sealed class ListAssignmentsByPayeeDefaultTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(ListAssignmentsByPayeeHandler Handler, Guid PayeeId);

    private static Harness BuildHarness(string dbName, int active, int deactivated)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var payee = Payee.Create(
            TenantId, "Rudolph", "CEO-001", null, null, "seed", Guid.NewGuid(), new DateTimeOffset(Now));
        db.Payees.Add(payee);

        for (var i = 0; i < active + deactivated; i++)
        {
            var plan = new PlanBuilder()
                .WithTenantId(TenantId).WithName($"Plan {i + 1}").WithCurrency("EUR")
                .WithPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).Build();
            db.CompensationPlans.Add(plan);

            // Period covers "today" so the handler's period filter (default: this month) keeps them —
            // this test is about the STATUS default, not the period one.
            var assignment = PlanAssignment.Create(
                TenantId, plan.Id, payee.Id,
                PayeeReference.Snapshot(payee.Id, "Rudolph", "CEO-001"),
                DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31)),
                "seed", Guid.NewGuid(), new DateTimeOffset(Now), Guid.NewGuid());

            if (i >= active)
                assignment.Deactivate("seed", new DateTimeOffset(Now), Guid.NewGuid());

            db.PlanAssignments.Add(assignment);
        }

        db.SaveChanges();

        var handler = new ListAssignmentsByPayeeHandler(
            db, Substitute.For<IAuthorizationService>(), new FakeClock(Now));

        return new Harness(handler, payee.Id);
    }

    private static PaginationQuery Query(string? status = null) =>
        new() { Page = 1, PageSize = 50, Status = status };

    // (a) Rudolph's real shape: some active, some deactivated, all covering today.
    [Fact]
    public async Task Without_a_status_only_active_assignments_are_returned()
    {
        var h = BuildHarness(nameof(Without_a_status_only_active_assignments_are_returned), active: 2, deactivated: 3);

        var result = await h.Handler.Handle(new ListAssignmentsByPayeeQuery(h.PayeeId, Query()), default);

        result.IsSuccess.Should().BeTrue();
        // The count drives the card's "N in this month" label, so it must narrow too — not just the page.
        result.Value!.TotalCount.Should().Be(2);
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items.Should().OnlyContain(a => a.Status == "Active");
    }

    // A blank string must behave like an absent one — no accidental "everything" through empty input.
    [Fact]
    public async Task A_blank_status_also_falls_back_to_active_only()
    {
        var h = BuildHarness(nameof(A_blank_status_also_falls_back_to_active_only), active: 2, deactivated: 3);

        var result = await h.Handler.Handle(new ListAssignmentsByPayeeQuery(h.PayeeId, Query("   ")), default);

        result.Value!.TotalCount.Should().Be(2);
    }

    // (b) An explicit status still filters to exactly that status.
    [Fact]
    public async Task An_explicit_deactivated_status_returns_only_deactivated()
    {
        var h = BuildHarness(nameof(An_explicit_deactivated_status_returns_only_deactivated), active: 2, deactivated: 3);

        var result = await h.Handler.Handle(
            new ListAssignmentsByPayeeQuery(h.PayeeId, Query("Deactivated")), default);

        result.Value!.TotalCount.Should().Be(3);
        result.Value.Items.Should().OnlyContain(a => a.Status == "Deactivated");
    }

    // (c) "all" is the explicit opt-in to every status.
    [Fact]
    public async Task The_all_sentinel_returns_every_status()
    {
        var h = BuildHarness(nameof(The_all_sentinel_returns_every_status), active: 2, deactivated: 3);

        var result = await h.Handler.Handle(
            new ListAssignmentsByPayeeQuery(h.PayeeId, Query(ListAssignmentsByPayeeHandler.AllStatuses)), default);

        result.Value!.TotalCount.Should().Be(5);
    }

    // An unrecognised value must land on the SAFE side, not on "everything" — the permissive outcome
    // has to be deliberate, never the result of a typo.
    [Fact]
    public async Task An_unrecognised_status_falls_back_to_active_rather_than_everything()
    {
        var h = BuildHarness(nameof(An_unrecognised_status_falls_back_to_active_rather_than_everything), active: 2, deactivated: 3);

        var result = await h.Handler.Handle(
            new ListAssignmentsByPayeeQuery(h.PayeeId, Query("Actve")), default);

        result.Value!.TotalCount.Should().Be(2);
    }
}
