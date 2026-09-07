using FluentAssertions;
using Wasnie.Application.Compensation.Handlers.Credits;
using Wasnie.Application.Compensation.Queries.Credits;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.IntegrationTests.Compensation;

/// <summary>
/// The order the assistant's transaction payload DECLARES, checked against real SQL Server.
///
/// ★★ WHY THIS IS AN INTEGRATION TEST AND NOT A UNIT ONE. `get_transaction` now ships
/// <c>commissionsOrderedBy: "MostRecentlyAllocatedFirst"</c> — a promise to the model about the array it
/// is reading, made because the model was inferring meaning from position over a list whose order nobody
/// had told it, and got a date from one row beside an amount from another
/// (docs/DIAG_ASSISTANT_CREDIT_PAYLOAD_OWNERSHIP.md).
///
/// A declared order that the data does not follow is worse than no declaration: it converts a guess into
/// a guarantee the payload cannot keep. The unit tests pin the tool against the in-memory provider; only
/// the real database can show that `AllocatedAt DESC` on a `datetimeoffset` column actually sorts that
/// way once EF has translated it.
///
/// ★ AND THE TOOL ASKS FOR THE SORT EXPLICITLY. It used to inherit ListCreditsHandler's default. That is
/// still the same order, but a default is somebody else's decision, and the day it changes the payload
/// starts lying without a single test going red. This pins the contract the tool actually requests.
/// </summary>
[Collection(PayoutEngineCollection.Name)]
public sealed class CreditOrderingContractTests(PayoutEngineFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    /// <summary>Permissions are not what this file is about; the ORDER is. Same double AntiDoublePayTests uses.</summary>
    private sealed class AlwaysAllowAuth : IAuthorizationService
    {
        public static readonly AlwaysAllowAuth Instance = new();
        public Task RequireAsync(string permission, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        // Added with IAuthorizationService.HasAsync: this double allows everything, so the
        // question answers the same way the enforcement does.
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static Credit SeedCredit(
        ApplicationDbContext db, Guid tenantId, Guid transactionId, Guid payeeId,
        decimal amount, DateTimeOffset allocatedAt)
    {
        var planId = Guid.NewGuid();
        var plan = Plan.Create(tenantId, $"Plan {planId:N}"[..20], "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), Eur,
            "seed", planId, Now, Guid.NewGuid());
        plan.AddRule("Commission", 1,
            new Measurement
            {
                Type = MeasurementType.Revenue,
                SourceField = "amount",
                Aggregation = MeasurementAggregation.Sum,
            },
            RateTable.Flat(0.04m));
        db.CompensationPlans.Add(plan);

        var ruleId = plan.Rules.First().Id;
        var credit = Credit.Allocate(tenantId, transactionId, payeeId, planId, ruleId,
            RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.04m), Trigger.Always(), allocatedAt),
            Money.Of(100_000m, Eur), Money.Of(amount, Eur),
            Percentage.FromPercent(100), CreditRole.Primary,
            "seed", Guid.NewGuid(), allocatedAt, Guid.NewGuid());

        db.Credits.Add(credit);
        return credit;
    }

    /// <summary>
    /// ★ THE REAL SHAPE. One sale, two payees, allocations two months apart — POL-8554 rebuilt against a
    /// real database. Newest first, and each amount still with its own date.
    /// </summary>
    [Fact]
    public async Task The_credits_of_one_transaction_come_back_newest_allocation_first()
    {
        var tenantId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var birgit = Payee.Create(tenantId, "Birgit Schneider", "DE-101", "de101@test.com",
                new DateOnly(2020, 1, 1), "seed", Guid.NewGuid(), Now);
            var adrian = Payee.Create(tenantId, "Adrian Dominguez", "NB-2001", "nb2001@test.com",
                new DateOnly(2020, 1, 1), "seed", Guid.NewGuid(), Now);
            db.Payees.AddRange(birgit, adrian);

            var tx = CompensationTransaction.Ingest(tenantId, "POL-8554", birgit.Id,
                Money.Of(85_700m, Eur), new DateOnly(2026, 6, 16), TransactionSource.Manual,
                "seed", transactionId, Now, Guid.NewGuid());
            db.CompensationTransactions.Add(tx);

            // Written OLDEST FIRST on purpose: if the handler ever stopped sorting, insertion order
            // would put the wrong row at index 0 and this test would catch it instead of agreeing.
            SeedCredit(db, tenantId, tx.Id, adrian.Id, 5_999.00m,
                new DateTimeOffset(2026, 6, 24, 8, 29, 25, TimeSpan.Zero));
            SeedCredit(db, tenantId, tx.Id, birgit.Id, 3_869.34m,
                new DateTimeOffset(2026, 8, 27, 15, 17, 37, TimeSpan.Zero));

            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            // Exactly the query GetTransactionTool.ReadCommissionAsync sends.
            var result = await new ListCreditsHandler(db, AlwaysAllowAuth.Instance).Handle(
                new ListCreditsQuery(new CreditFilterQuery
                {
                    Page = 1,
                    PageSize = 25,
                    Reference = "POL-8554",
                    Status = "All",
                    SortBy = "allocatedAt",
                    SortOrder = "desc",
                }),
                CancellationToken.None);

            var rows = result.Value!.Items;
            rows.Should().HaveCount(2);

            rows.Select(r => r.AllocatedAt.UtcDateTime.Date)
                .Should().ContainInOrder(new DateTime(2026, 8, 27), new DateTime(2026, 6, 24));

            // ★ THE PAIRING, not just the set. The original failure kept every value and crossed them,
            // so asserting the amounts alone would have passed on a wrong payload.
            rows.Select(r => (r.CreditedAmount, r.AllocatedAt.UtcDateTime.Date))
                .Should().ContainInOrder(
                    (3_869.34m, new DateTime(2026, 8, 27)),
                    (5_999.00m, new DateTime(2026, 6, 24)));
        }
    }

    /// <summary>
    /// A superseded credit stays in the answer — <c>Status = "All"</c> — because "there is another
    /// credit on this sale and it is dead" is exactly the fact the assistant needed and did not have.
    /// </summary>
    [Fact]
    public async Task A_superseded_credit_is_still_returned_and_still_marked()
    {
        var tenantId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = Payee.Create(tenantId, "Someone", "SOM-9", "som9@test.com",
                new DateOnly(2020, 1, 1), "seed", Guid.NewGuid(), Now);
            db.Payees.Add(payee);

            var tx = CompensationTransaction.Ingest(tenantId, "SUP-1", payee.Id,
                Money.Of(1_000m, Eur), new DateOnly(2026, 6, 16), TransactionSource.Manual,
                "seed", Guid.NewGuid(), Now, Guid.NewGuid());
            db.CompensationTransactions.Add(tx);

            var dead = SeedCredit(db, tenantId, tx.Id, payee.Id, 40m,
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
            dead.Supersede("Reassigned.", Now, Guid.NewGuid());

            SeedCredit(db, tenantId, tx.Id, payee.Id, 60m,
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await new ListCreditsHandler(db, AlwaysAllowAuth.Instance).Handle(
                new ListCreditsQuery(new CreditFilterQuery
                {
                    Page = 1, PageSize = 25, Reference = "SUP-1", Status = "All",
                    SortBy = "allocatedAt", SortOrder = "desc",
                }),
                CancellationToken.None);

            var rows = result.Value!.Items;
            rows.Should().HaveCount(2);
            rows[0].IsSuperseded.Should().BeFalse("newest first, and the live one is the newest here");
            rows[1].IsSuperseded.Should().BeTrue();
        }
    }
}
