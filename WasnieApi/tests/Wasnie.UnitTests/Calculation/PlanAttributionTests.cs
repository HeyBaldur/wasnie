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

        var resolved = PlanAssignmentResolver.Resolve([wide, narrow], TxDate, "EUR", currencies);

        resolved.Should().BeSameAs(narrow);
    }

    [Fact]
    public void Resolve_without_selection_returns_null_when_no_plan_matches_the_currency()
    {
        var eur = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var currencies = new Dictionary<Guid, string> { [eur.PlanId] = "EUR" };

        PlanAssignmentResolver.Resolve([eur], TxDate, "USD", currencies).Should().BeNull();
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
            [good, inactive, outOfPeriod, otherCurrency], TxDate, "EUR", currencies);

        candidates.Should().ContainSingle().Which.Should().BeSameAs(good);
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
        PlanAssignmentResolver.Resolve([wide, narrow], TxDate, "EUR", currencies).Should().BeSameAs(narrow);

        var resolution = PlanAssignmentResolver.ResolveSelected(
            [wide, narrow], TxDate, "EUR", currencies, wide.Id);

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
            [chosen, other], TxDate, "EUR", currencies, chosen.Id);

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
            [chosen], TxDate, "EUR", currencies, chosen.Id);

        resolution.IsAccepted.Should().BeFalse();
        resolution.RejectionReason.Should().Contain("transaction date");
    }

    [Fact]
    public void ResolveSelected_rejects_a_plan_in_a_different_currency()
    {
        var chosen = Assignment(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var currencies = new Dictionary<Guid, string> { [chosen.PlanId] = "USD" };

        var resolution = PlanAssignmentResolver.ResolveSelected(
            [chosen], TxDate, "EUR", currencies, chosen.Id);

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
            [mine], TxDate, "EUR", currencies, Guid.NewGuid());

        resolution.IsAccepted.Should().BeFalse();
        resolution.RejectionReason.Should().Contain("no longer belongs");
    }
}
