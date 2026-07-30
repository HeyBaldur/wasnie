using FluentAssertions;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// Money-critical: which PlanAssignment carries a transaction decides which rate table is applied and
/// therefore how much commission a rep is paid. Two paths must hold simultaneously —
/// an explicit admin selection is obeyed exactly, and everything WITHOUT a selection keeps resolving
/// through the historical Pattern B tie-break with no behaviour change at all.
/// </summary>
public sealed class PlanAttributionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PayeeId = Guid.NewGuid();
    private static readonly DateOnly TxDate = new(2026, 6, 15);
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);

    /// <summary>No plan is retired — the ordinary case every pre-existing test here describes.</summary>
    private static readonly IReadOnlySet<Guid> NoneArchived = new HashSet<Guid>();

    private static PlanAssignment Assignment(Guid planId, DateOnly start, DateOnly end, bool active = true)
    {
        var assignment = PlanAssignment.Create(
            TenantId, planId, PayeeId,
            PayeeReference.Snapshot(PayeeId, "Test Payee", "E1"),
            DateRange.Of(start, end),
            "seed", Guid.NewGuid(), Now, Guid.NewGuid());

        if (!active)
            assignment.Deactivate("seed", Now, Guid.NewGuid());

        return assignment;
    }

    // ── No selection: the pre-existing behaviour, unchanged (regression guard) ─────────────────

    [Fact]
    public void Resolve_without_selection_still_uses_the_shortest_period_tie_break()
    {
        var wide = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var narrow = Assignment(Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var currencies = new Dictionary<Guid, string>
        {
            [wide.PlanId] = "EUR",
            [narrow.PlanId] = "EUR",
        };

        var resolved = PlanAssignmentResolver.Resolve([wide, narrow], TxDate, "EUR", currencies, NoneArchived);

        resolved.Should().BeSameAs(narrow);
    }

    [Fact]
    public void Resolve_without_selection_returns_null_when_no_plan_matches_the_currency()
    {
        var eur = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var currencies = new Dictionary<Guid, string> { [eur.PlanId] = "EUR" };

        PlanAssignmentResolver.Resolve([eur], TxDate, "USD", currencies, NoneArchived).Should().BeNull();
    }

    // ── Candidates: exactly what the selector may offer ───────────────────────────────────────

    [Fact]
    public void Candidates_excludes_inactive_out_of_period_and_other_currency_assignments()
    {
        var good = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var inactive = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), active: false);
        var outOfPeriod = Assignment(Guid.NewGuid(), new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var otherCurrency = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var currencies = new Dictionary<Guid, string>
        {
            [good.PlanId] = "EUR",
            [inactive.PlanId] = "EUR",
            [outOfPeriod.PlanId] = "EUR",
            [otherCurrency.PlanId] = "USD",
        };

        var candidates = PlanAssignmentResolver.Candidates(
            [good, inactive, outOfPeriod, otherCurrency], TxDate, "EUR", currencies, NoneArchived);

        candidates.Should().ContainSingle().Which.Should().BeSameAs(good);
    }

    // ── An ARCHIVED plan pays nothing, whatever its assignment says ───────────────────────────
    // The second layer of the archived-plan guard. The first (the assignment handler refuses to
    // assign to an archived plan; archiving deactivates the assignments that exist) acts at WRITE
    // time and therefore cannot cover an assignment that outlived its plan's archiving — which is
    // exactly what happened on 2026-07-22, when the CRM sync credited €25,560 against a plan that
    // had been archived an hour earlier. Eligibility now reads the plan's status, not only the
    // assignment's.

    [Fact]
    public void Candidates_excludes_an_ACTIVE_assignment_whose_plan_is_archived()
    {
        var live = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var retired = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var currencies = new Dictionary<Guid, string>
        {
            [live.PlanId] = "EUR",
            [retired.PlanId] = "EUR",
        };
        var archived = new HashSet<Guid> { retired.PlanId };

        var candidates = PlanAssignmentResolver.Candidates(
            [live, retired], TxDate, "EUR", currencies, archived);

        // The retired assignment is Active, in period and in the right currency — and still not a
        // candidate. Only the plan's status keeps it out.
        candidates.Should().ContainSingle().Which.Should().BeSameAs(live);
    }

    [Fact]
    public void Candidates_returns_nothing_when_the_only_eligible_plan_is_archived()
    {
        var retired = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var currencies = new Dictionary<Guid, string> { [retired.PlanId] = "EUR" };

        var candidates = PlanAssignmentResolver.Candidates(
            [retired], TxDate, "EUR", currencies, new HashSet<Guid> { retired.PlanId });

        // No candidates → the transaction stays Pending and uncredited. That is the money outcome
        // that matters: nothing is written against a retired plan.
        candidates.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_never_picks_an_archived_plan()
    {
        var retired = Assignment(Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var live = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var currencies = new Dictionary<Guid, string>
        {
            [retired.PlanId] = "EUR",
            [live.PlanId] = "EUR",
        };

        // The tie-break prefers the SHORTEST period, which is the archived one — so this also proves
        // the exclusion happens before the tie-break, not after it.
        PlanAssignmentResolver.Resolve([retired, live], TxDate, "EUR", currencies, NoneArchived)
            .Should().BeSameAs(retired, "sanity: without the guard the archived plan would win");

        PlanAssignmentResolver
            .Resolve([retired, live], TxDate, "EUR", currencies, new HashSet<Guid> { retired.PlanId })
            .Should().BeSameAs(live);
    }

    [Fact]
    public void ResolveSelected_rejects_an_archived_plan_and_says_why()
    {
        var chosen = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var currencies = new Dictionary<Guid, string> { [chosen.PlanId] = "EUR" };

        var resolution = PlanAssignmentResolver.ResolveSelected(
            [chosen], TxDate, "EUR", currencies, new HashSet<Guid> { chosen.PlanId }, chosen.Id);

        resolution.IsAccepted.Should().BeFalse();
        resolution.Assignment.Should().BeNull();
        // Named for what it is — not left to fall through the currency check with a misleading reason.
        resolution.RejectionReason.Should().Contain("archived");
    }

    [Fact]
    public void An_active_plan_is_unaffected_by_the_archived_guard()
    {
        // The happy path, stated explicitly: the guard must cost nothing to everyone else.
        var live = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var currencies = new Dictionary<Guid, string> { [live.PlanId] = "EUR" };
        var someOtherArchivedPlan = new HashSet<Guid> { Guid.NewGuid() };

        PlanAssignmentResolver.Candidates([live], TxDate, "EUR", currencies, someOtherArchivedPlan)
            .Should().ContainSingle().Which.Should().BeSameAs(live);

        PlanAssignmentResolver.ResolveSelected(
                [live], TxDate, "EUR", currencies, someOtherArchivedPlan, live.Id)
            .IsAccepted.Should().BeTrue();
    }

    // ── Explicit selection: obeyed exactly, or rejected loudly ────────────────────────────────

    // The bug this WI fixes: the engine's tie-break would have picked `narrow`; the admin said `wide`.
    [Fact]
    public void ResolveSelected_honours_the_admin_choice_over_the_tie_break()
    {
        var wide = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var narrow = Assignment(Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var currencies = new Dictionary<Guid, string>
        {
            [wide.PlanId] = "EUR",
            [narrow.PlanId] = "EUR",
        };

        // Sanity: without a selection the engine would pick the other one.
        PlanAssignmentResolver.Resolve([wide, narrow], TxDate, "EUR", currencies, NoneArchived).Should().BeSameAs(narrow);

        var resolution = PlanAssignmentResolver.ResolveSelected(
            [wide, narrow], TxDate, "EUR", currencies, NoneArchived, wide.Id);

        resolution.IsAccepted.Should().BeTrue();
        resolution.Assignment.Should().BeSameAs(wide);
    }

    [Fact]
    public void ResolveSelected_rejects_a_deactivated_assignment_instead_of_falling_back()
    {
        var chosen = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), active: false);
        var other = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var currencies = new Dictionary<Guid, string>
        {
            [chosen.PlanId] = "EUR",
            [other.PlanId] = "EUR",
        };

        var resolution = PlanAssignmentResolver.ResolveSelected(
            [chosen, other], TxDate, "EUR", currencies, NoneArchived, chosen.Id);

        resolution.IsAccepted.Should().BeFalse();
        // Critically: it does NOT silently return `other`.
        resolution.Assignment.Should().BeNull();
        resolution.RejectionReason.Should().Contain("no longer active");
    }

    [Fact]
    public void ResolveSelected_rejects_an_assignment_that_no_longer_covers_the_date()
    {
        var chosen = Assignment(Guid.NewGuid(), new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var currencies = new Dictionary<Guid, string> { [chosen.PlanId] = "EUR" };

        var resolution = PlanAssignmentResolver.ResolveSelected(
            [chosen], TxDate, "EUR", currencies, NoneArchived, chosen.Id);

        resolution.IsAccepted.Should().BeFalse();
        resolution.RejectionReason.Should().Contain("transaction date");
    }

    [Fact]
    public void ResolveSelected_rejects_a_plan_in_a_different_currency()
    {
        var chosen = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var currencies = new Dictionary<Guid, string> { [chosen.PlanId] = "USD" };

        var resolution = PlanAssignmentResolver.ResolveSelected(
            [chosen], TxDate, "EUR", currencies, NoneArchived, chosen.Id);

        resolution.IsAccepted.Should().BeFalse();
        resolution.RejectionReason.Should().Contain("EUR");
    }

    // Happens when a transaction is reassigned to another payee — the id names a stranger's assignment.
    [Fact]
    public void ResolveSelected_rejects_an_assignment_that_is_not_the_payees()
    {
        var mine = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var currencies = new Dictionary<Guid, string> { [mine.PlanId] = "EUR" };

        var resolution = PlanAssignmentResolver.ResolveSelected(
            [mine], TxDate, "EUR", currencies, NoneArchived, Guid.NewGuid());

        resolution.IsAccepted.Should().BeFalse();
        resolution.RejectionReason.Should().Contain("no longer belongs");
    }
}
