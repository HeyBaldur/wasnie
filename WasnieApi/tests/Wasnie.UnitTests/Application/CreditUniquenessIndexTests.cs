using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Guards the DECLARATIVE half of anti-double-pay.
///
/// Until this index existed, the only thing stopping a duplicate Credit was the procedural batch guard
/// in ProcessPendingTransactionsJobHandler. Nothing in the database enforced it, so a wrong key there
/// would have produced duplicate credits silently — and they would have been paid.
///
/// These assertions are on the EF MODEL, not on a live database: the in-memory provider does not
/// enforce unique indexes, so an insert-based test would pass whether or not the index exists and
/// would prove nothing. What can be protected here is that the index definition itself is not
/// weakened or removed — its actual enforcement is verified against SQL Server via sys.indexes.
/// </summary>
public sealed class CreditUniquenessIndexTests
{
    private static IModel BuildModel()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(Guid.NewGuid());

        using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=none;Database=none;")   // never opened — model metadata only
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        return db.Model;
    }

    private static IIndex? FindUniquenessIndex() =>
        BuildModel()
            .FindEntityType(typeof(Credit))!
            .GetIndexes()
            .SingleOrDefault(i => i.IsUnique);

    [Fact]
    public void Credit_has_exactly_one_unique_index()
    {
        FindUniquenessIndex().Should().NotBeNull(
            "the declarative anti-double-pay guard must exist — without it a duplicate credit is only " +
            "prevented by application code");
    }

    [Fact]
    public void The_uniqueness_key_is_transaction_plus_plan_plus_rule_scoped_to_the_tenant()
    {
        var index = FindUniquenessIndex()!;

        index.Properties.Select(p => p.Name).Should().Equal(
            nameof(Credit.TenantId),
            nameof(Credit.TransactionId),
            nameof(Credit.PlanId),
            nameof(Credit.RuleId));
    }

    /// <summary>
    /// The filter is what keeps RecalculateCredits working: it supersedes a credit and re-creates its
    /// replacement with the SAME (transaction, plan, rule). Without the filter every recalculation
    /// would violate the index.
    /// </summary>
    [Fact]
    public void Superseded_credits_are_exempt_so_recalculation_still_works()
    {
        FindUniquenessIndex()!.GetFilter().Should().Be("[SupersededAt] IS NULL");
    }

    /// <summary>
    /// A consumed credit is one that a Paid payout already consumed. It MUST still occupy the key —
    /// it is exactly the row a duplicate must never be created against.
    /// </summary>
    [Fact]
    public void Consumed_credits_still_occupy_the_key()
    {
        FindUniquenessIndex()!.GetFilter().Should().NotContain("ConsumedAt");
    }
}
