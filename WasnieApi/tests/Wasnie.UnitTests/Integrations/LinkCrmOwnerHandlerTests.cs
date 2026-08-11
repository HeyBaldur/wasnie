using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Application.Integrations.HubSpot;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Integrations.Crm;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Integrations;

/// <summary>
/// FASE 2d tests: manual owner→payee linking + the retroactive reassignment policy (only Unassigned,
/// never Paid/assigned).
/// </summary>
public sealed class LinkCrmOwnerHandlerTests
{
    private const string Source = "HubSpot";
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 10, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required ApplicationDbContext Db { get; init; }
        public required LinkCrmOwnerHandler Handler { get; init; }
        public required ICrmDealSource DealSource { get; init; }
        public required Guid TenantId { get; init; }
    }

    private static Harness Build(string dbName, Guid tenantId)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());
        var clock = new FakeClock(Now.UtcDateTime);
        var guid = new FakeGuidGenerator();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-1");
        var authz = Substitute.For<IAuthorizationService>();
        var dealSource = Substitute.For<ICrmDealSource>();
        dealSource.SourceName.Returns(Source);

        var handler = new LinkCrmOwnerHandler(db, tenantCtx, currentUser, clock, guid, authz,
            new Wasnie.UnitTests.TestDoubles.FakePaidPlanGate(), dealSource);
        return new Harness { Db = db, Handler = handler, DealSource = dealSource, TenantId = tenantId };
    }

    private static Payee SeedPayee(ApplicationDbContext db, Guid tenantId, string code)
    {
        var p = Payee.Create(tenantId, $"Payee {code}", code, null, null, "seed", Guid.NewGuid(), Now);
        db.Payees.Add(p);
        db.SaveChanges();
        return p;
    }

    private static void SeedCrmTx(ApplicationDbContext db, Guid tenantId, string externalId, Guid? payeeId)
    {
        var tx = CompensationTransaction.Ingest(
            tenantId, $"HUBSPOT-{externalId}", payeeId, Money.Of(100m, "USD"),
            new DateOnly(2026, 6, 1), TransactionSource.CrmSync, "seed",
            Guid.NewGuid(), Now, Guid.NewGuid(), externalId: externalId);
        db.CompensationTransactions.Add(tx);
        db.SaveChanges();
    }

    [Fact]
    public async Task Linking_an_owner_creates_a_manual_mapping()
    {
        var tenantId = Guid.NewGuid();
        var h = Build(nameof(Linking_an_owner_creates_a_manual_mapping), tenantId);
        var payee = SeedPayee(h.Db, tenantId, "E1");
        h.DealSource.GetClosedWonDealsAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CrmDeal>());

        var result = await h.Handler.Handle(new LinkCrmOwnerCommand("O1", payee.Id, false), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ReassignedTransactions.Should().Be(0);
        var mapping = await h.Db.CrmOwnerMappings.SingleAsync();
        mapping.CrmOwnerId.Should().Be("O1");
        mapping.PayeeId.Should().Be(payee.Id);
        mapping.MatchMethod.Should().Be(CrmOwnerMatchMethod.Manual);
    }

    [Fact]
    public async Task Reassign_moves_only_unassigned_transactions_of_that_owner_and_never_touches_others()
    {
        var tenantId = Guid.NewGuid();
        var h = Build(nameof(Reassign_moves_only_unassigned_transactions_of_that_owner_and_never_touches_others), tenantId);
        var payee = SeedPayee(h.Db, tenantId, "E1");
        var otherPayee = SeedPayee(h.Db, tenantId, "E2");

        SeedCrmTx(h.Db, tenantId, "D1", payeeId: null);              // owner O1, unassigned → reassign
        SeedCrmTx(h.Db, tenantId, "D2", payeeId: null);              // owner O1, unassigned → reassign
        SeedCrmTx(h.Db, tenantId, "D3", payeeId: otherPayee.Id);     // owner O1, already assigned → untouched
        SeedCrmTx(h.Db, tenantId, "D9", payeeId: null);              // different owner → untouched

        // O1 owns D1, D2, D3 (not D9).
        h.DealSource.GetClosedWonDealsAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new CrmDeal("D1", "d1", 100m, "USD", new DateOnly(2026, 6, 1), "O1"),
            new CrmDeal("D2", "d2", 100m, "USD", new DateOnly(2026, 6, 1), "O1"),
            new CrmDeal("D3", "d3", 100m, "USD", new DateOnly(2026, 6, 1), "O1"),
        });

        var result = await h.Handler.Handle(new LinkCrmOwnerCommand("O1", payee.Id, true), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ReassignedTransactions.Should().Be(2);

        var byRef = await h.Db.CompensationTransactions.ToDictionaryAsync(t => t.ReferenceNumber);
        byRef["HUBSPOT-D1"].PayeeId.Should().Be(payee.Id);
        byRef["HUBSPOT-D2"].PayeeId.Should().Be(payee.Id);
        byRef["HUBSPOT-D3"].PayeeId.Should().Be(otherPayee.Id); // untouched (already assigned)
        byRef["HUBSPOT-D9"].PayeeId.Should().BeNull();           // untouched (different owner)
    }

    [Fact]
    public async Task Linking_an_already_mapped_owner_fails()
    {
        var tenantId = Guid.NewGuid();
        var h = Build(nameof(Linking_an_already_mapped_owner_fails), tenantId);
        var payee = SeedPayee(h.Db, tenantId, "E1");
        h.Db.CrmOwnerMappings.Add(CrmOwnerMapping.Create(
            Guid.NewGuid(), tenantId, Source, "O1", payee.Id, CrmOwnerMatchMethod.Email, "seed", Now));
        h.Db.SaveChanges();

        var result = await h.Handler.Handle(new LinkCrmOwnerCommand("O1", payee.Id, false), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already linked");
    }

    [Fact]
    public async Task Linking_a_missing_payee_fails()
    {
        var tenantId = Guid.NewGuid();
        var h = Build(nameof(Linking_a_missing_payee_fails), tenantId);

        var result = await h.Handler.Handle(new LinkCrmOwnerCommand("O1", Guid.NewGuid(), false), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Payee not found");
        (await h.Db.CrmOwnerMappings.AnyAsync()).Should().BeFalse();
    }
}
