using FluentAssertions;
using Wasnie.Application.Compensation.Common;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The detection rule behind the fail-loud block and the dashboard card. Both read from here, so the
/// card can never claim an ambiguity the engine doesn't act on (or miss one it does).
/// </summary>
public sealed class AmbiguousAttributionSpecTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PayeeId = Guid.NewGuid();
    private static readonly DateOnly TxDate = new(2026, 6, 15);
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);

    private static PlanAssignment Assignment(Guid planId, bool active = true, int year = 2026)
    {
        var assignment = PlanAssignment.Create(
            TenantId, planId, PayeeId,
            PayeeReference.Snapshot(PayeeId, "Test Payee", "E1"),
            DateRange.Of(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31)),
            "seed", Guid.NewGuid(), Now, Guid.NewGuid());

        if (!active) assignment.Deactivate("seed", Now, Guid.NewGuid());
        return assignment;
    }

    private static CompensationTransaction Transaction(Guid? selected = null) =>
        CompensationTransaction.Ingest(
            TenantId, "REF-1", PayeeId, Money.Of(50m, "EUR"), TxDate,
            TransactionSource.EtlImport, "import", Guid.NewGuid(), Now, Guid.NewGuid(),
            quantity: 112, selectedPlanAssignmentId: selected);

    [Fact]
    public void Two_eligible_plans_and_no_choice_is_ambiguous()
    {
        var a = Assignment(Guid.NewGuid());
        var b = Assignment(Guid.NewGuid());
        var currencies = new Dictionary<Guid, string> { [a.PlanId] = "EUR", [b.PlanId] = "EUR" };

        var candidates = AmbiguousAttributionSpec.AmbiguousCandidates(Transaction(), [a, b], currencies);

        candidates.Should().HaveCount(2);
    }

    [Fact]
    public void One_eligible_plan_is_not_ambiguous()
    {
        var a = Assignment(Guid.NewGuid());
        var currencies = new Dictionary<Guid, string> { [a.PlanId] = "EUR" };

        AmbiguousAttributionSpec.IsAmbiguous(Transaction(), [a], currencies).Should().BeFalse();
    }

    [Fact]
    public void No_eligible_plan_is_not_ambiguous()
    {
        AmbiguousAttributionSpec
            .IsAmbiguous(Transaction(), [], new Dictionary<Guid, string>())
            .Should().BeFalse();
    }

    // The manual path's guarantee: a declared plan removes the ambiguity by definition.
    [Fact]
    public void A_declared_plan_is_never_ambiguous_even_with_several_eligible_plans()
    {
        var a = Assignment(Guid.NewGuid());
        var b = Assignment(Guid.NewGuid());
        var currencies = new Dictionary<Guid, string> { [a.PlanId] = "EUR", [b.PlanId] = "EUR" };

        AmbiguousAttributionSpec
            .IsAmbiguous(Transaction(selected: a.Id), [a, b], currencies)
            .Should().BeFalse();
    }

    // Ambiguity uses the ENGINE's eligibility rule — an assignment the engine would ignore must not
    // create a phantom ambiguity in the dashboard.
    [Fact]
    public void A_deactivated_second_assignment_does_not_create_ambiguity()
    {
        var active = Assignment(Guid.NewGuid());
        var deactivated = Assignment(Guid.NewGuid(), active: false);
        var currencies = new Dictionary<Guid, string>
        {
            [active.PlanId] = "EUR",
            [deactivated.PlanId] = "EUR",
        };

        AmbiguousAttributionSpec
            .IsAmbiguous(Transaction(), [active, deactivated], currencies)
            .Should().BeFalse();
    }

    [Fact]
    public void A_second_plan_in_another_currency_does_not_create_ambiguity()
    {
        var eur = Assignment(Guid.NewGuid());
        var usd = Assignment(Guid.NewGuid());
        var currencies = new Dictionary<Guid, string> { [eur.PlanId] = "EUR", [usd.PlanId] = "USD" };

        AmbiguousAttributionSpec
            .IsAmbiguous(Transaction(), [eur, usd], currencies)
            .Should().BeFalse();
    }

    [Fact]
    public void A_second_assignment_outside_the_transaction_date_does_not_create_ambiguity()
    {
        var covering = Assignment(Guid.NewGuid());
        var otherYear = Assignment(Guid.NewGuid(), year: 2025);
        var currencies = new Dictionary<Guid, string>
        {
            [covering.PlanId] = "EUR",
            [otherYear.PlanId] = "EUR",
        };

        AmbiguousAttributionSpec
            .IsAmbiguous(Transaction(), [covering, otherYear], currencies)
            .Should().BeFalse();
    }

    [Fact]
    public void SkipReason_names_the_number_of_competing_plans()
    {
        AmbiguousAttributionSpec.SkipReason(3).Should().Contain("3 eligible plans");
    }
}
