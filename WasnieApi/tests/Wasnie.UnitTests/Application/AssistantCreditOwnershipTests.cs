using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Assistant.Tools;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Credits;
using Wasnie.Application.Compensation.Handlers.Payouts;
using Wasnie.Application.Compensation.Handlers.Transactions;
using Wasnie.Application.Compensation.Queries.Credits;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Whose credit is whose, in the payload the assistant reads.
///
/// ★★ THE BUG THESE PIN. Credits are looked up BY TRANSACTION, so a sale reassigned from one person to
/// another returns BOTH rows — and the payload carried nothing to tell them apart, because
/// <c>PayeeId</c> is <c>[JsonIgnore]</c>. Asked about one payee and handed two people's credits, the
/// assistant reported the rows SHUFFLED: the amounts in array order, the dates in reverse, and
/// `superseded` on the wrong one. Every individual value was real; the pairing was invented, and it
/// sent a real administrator to calculate the wrong month
/// (docs/DIAG_ASSISTANT_CREDIT_PAYLOAD_OWNERSHIP.md).
///
/// ★ AND THE FIX IS DATA, NOT A PROMPT RULE. No instruction could have caught this: the model was not
/// given anything to distinguish the rows with. The label is an OPAQUE token, never a name — see
/// docs/Legal.md §3.2, whose already-decided mitigation is to send an opaque reference INSTEAD of the
/// payee's name, with the DPA still unsigned.
/// </summary>
public sealed class AssistantCreditOwnershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    private sealed class RoleAuthorization(params string[] permissions) : IAuthorizationService
    {
        private readonly HashSet<string> _granted = new(permissions, StringComparer.OrdinalIgnoreCase);

        public Task RequireAsync(string permission, CancellationToken cancellationToken = default) =>
            _granted.Contains(permission)
                ? Task.CompletedTask
                : throw new ForbiddenException(permission);

        // Same set, asked instead of enforced.
        public Task<bool> HasAsync(string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(_granted.Contains(permission));
    }

    /// <summary>Real handlers, so the guards and the sort order are the production ones.</summary>
    private sealed class HandlerSender(IApplicationDbContext db, IAuthorizationService auth) : ISender
    {
        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default) => request switch
        {
            ListTransactionsQuery q => (TResponse)(object)await new ListTransactionsHandler(db, auth)
                .Handle(q, cancellationToken),
            ListCreditsQuery q => (TResponse)(object)await new ListCreditsHandler(db, auth)
                .Handle(q, cancellationToken),
            // The settlement walk always runs once there are credits. The real handler, so the
            // Payouts.Read guard is the production one; no payouts are seeded, so it finds none and the
            // walk reports "not yet paid" — which is the state these fixtures are in anyway.
            ListPayoutsQuery q => (TResponse)(object)await new ListPayoutsHandler(db, auth)
                .Handle(q, cancellationToken),
            _ => throw new NotSupportedException($"Unexpected query {request.GetType().Name}."),
        };

        public Task<object?> Send(object r, CancellationToken c = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest r, CancellationToken c = default) where TRequest : IRequest =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> r, CancellationToken c = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object r, CancellationToken c = default) =>
            throw new NotSupportedException();
    }

    private sealed record Harness(ApplicationDbContext Db, GetTransactionTool Tool, Guid TenantId);

    private static Harness Build(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(AssistantCreditOwnershipTests)}.{dbName}").Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var sender = new HandlerSender(
            db, new RoleAuthorization(
                Permission.TransactionsRead, Permission.CreditsRead, Permission.PayoutsRead));

        return new Harness(
            db, new GetTransactionTool(sender, NullLogger<GetTransactionTool>.Instance), tenantId);
    }

    private static Payee AddPayee(Harness h, string name, string code)
    {
        var payee = Payee.Create(h.TenantId, name, code, $"{code}@acme.com",
            new DateOnly(2020, 1, 1), "seed", Guid.NewGuid(), Now);
        h.Db.Payees.Add(payee);
        h.Db.SaveChanges();
        return payee;
    }

    private static CompensationTransaction AddTransaction(Harness h, string reference, Guid payeeId)
    {
        var tx = CompensationTransaction.Ingest(h.TenantId, reference, payeeId,
            Money.Of(85_700m, Eur), new DateOnly(2026, 6, 16), TransactionSource.Manual,
            "seed", Guid.NewGuid(), Now, Guid.NewGuid());
        h.Db.CompensationTransactions.Add(tx);
        h.Db.SaveChanges();
        return tx;
    }

    /// <summary>A credit on that sale, for that person, allocated on that day.</summary>
    private static Credit AddCredit(
        Harness h, CompensationTransaction tx, Guid payeeId, decimal amount,
        DateTimeOffset allocatedAt, bool superseded = false)
    {
        var planId = Guid.NewGuid();
        var plan = Plan.Create(h.TenantId, "EU Accelerator Q2 2026", "desc",
            DateRange.Of(new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30)), Eur,
            "seed", planId, Now, Guid.NewGuid());
        plan.AddRule("Tier 1: 4% up to quota", 1,
            new Measurement
            {
                Type = MeasurementType.Revenue,
                SourceField = "amount",
                Aggregation = MeasurementAggregation.Sum,
            },
            RateTable.Flat(0.04m));
        h.Db.CompensationPlans.Add(plan);

        var ruleId = plan.Rules.First().Id;
        var credit = Credit.Allocate(h.TenantId, tx.Id, payeeId, planId, ruleId,
            RuleSnapshot.Freeze(ruleId, planId, 1, "Tier 1: 4% up to quota",
                RateTable.Flat(0.04m), Trigger.Always(), allocatedAt),
            Money.Of(85_700m, Eur), Money.Of(amount, Eur),
            Percentage.FromPercent(100), CreditRole.Primary,
            "seed", Guid.NewGuid(), allocatedAt, Guid.NewGuid());

        if (superseded)
            credit.Supersede("Transaction reassigned.", allocatedAt.AddDays(1), Guid.NewGuid());

        h.Db.Credits.Add(credit);
        h.Db.SaveChanges();
        return credit;
    }

    private static async Task<JsonElement> RunAsync(Harness h, string reference)
    {
        var json = await h.Tool.RunAsync($$"""{"reference":"{{reference}}"}""", CancellationToken.None);
        return JsonDocument.Parse(json).RootElement;
    }

    private static List<JsonElement> Commissions(JsonElement payload) =>
        payload.GetProperty("commissions").EnumerateArray().Select(e => e.Clone()).ToList();

    // ══ ★ The real case ═══════════════════════════════════════════════════════

    /// <summary>
    /// ★★ POL-8554, REBUILT. Birgit's active €3,869.34 and Adrian's superseded €5,999 on one sale.
    /// Before this field the two rows were indistinguishable and the assistant fused them.
    /// </summary>
    [Fact]
    public async Task Two_payees_on_one_sale_come_back_distinguishable()
    {
        var h = Build(nameof(Two_payees_on_one_sale_come_back_distinguishable));
        var birgit = AddPayee(h, "Birgit Schneider", "DE-101");
        var adrian = AddPayee(h, "Adrian Dominguez", "NB-2001");

        var tx = AddTransaction(h, "POL-8554", birgit.Id);
        AddCredit(h, tx, adrian.Id, 5_999.00m, new DateTimeOffset(2026, 6, 24, 8, 29, 25, TimeSpan.Zero),
            superseded: true);
        AddCredit(h, tx, birgit.Id, 3_869.34m, new DateTimeOffset(2026, 8, 27, 15, 17, 37, TimeSpan.Zero));

        var rows = Commissions(await RunAsync(h, "POL-8554"));

        rows.Should().HaveCount(2);
        rows.Select(r => r.GetProperty("payeeRef").GetString())
            .Should().OnlyHaveUniqueItems("two people must never share a label");

        // The row that belongs to the person the question is about.
        var mine = rows.Single(r => r.GetProperty("payeeRef").GetString() == "TransactionPayee");
        mine.GetProperty("commissionAmount").GetDecimal().Should().Be(3_869.34m);
        mine.GetProperty("creditAllocatedAt").GetString().Should().Be("2026-08-27");
        mine.GetProperty("creditIsSuperseded").GetBoolean().Should().BeFalse();

        // And the one that does not — the fact the whole answer turned on.
        var theirs = rows.Single(r => r.GetProperty("payeeRef").GetString()!.StartsWith("OtherPayee"));
        theirs.GetProperty("commissionAmount").GetDecimal().Should().Be(5_999.00m);
        theirs.GetProperty("creditAllocatedAt").GetString().Should().Be("2026-06-24");
        theirs.GetProperty("creditIsSuperseded").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// ★ EVERY VALUE STAYS WITH ITS OWN ROW. The original failure kept both amounts and both dates but
    /// crossed them, so asserting the SET of values would have passed while the payload was wrong.
    /// These assert the PAIRS.
    /// </summary>
    [Fact]
    public async Task Each_amount_stays_with_its_own_date_and_its_own_superseded_flag()
    {
        var h = Build(nameof(Each_amount_stays_with_its_own_date_and_its_own_superseded_flag));
        var birgit = AddPayee(h, "Birgit Schneider", "DE-101");
        var adrian = AddPayee(h, "Adrian Dominguez", "NB-2001");

        var tx = AddTransaction(h, "POL-8554", birgit.Id);
        AddCredit(h, tx, adrian.Id, 5_999.00m, new DateTimeOffset(2026, 6, 24, 8, 0, 0, TimeSpan.Zero),
            superseded: true);
        AddCredit(h, tx, birgit.Id, 3_869.34m, new DateTimeOffset(2026, 8, 27, 15, 0, 0, TimeSpan.Zero));

        var pairs = Commissions(await RunAsync(h, "POL-8554"))
            .Select(r => (
                Amount: r.GetProperty("commissionAmount").GetDecimal(),
                Date: r.GetProperty("creditAllocatedAt").GetString(),
                Superseded: r.GetProperty("creditIsSuperseded").GetBoolean()))
            .ToList();

        pairs.Should().BeEquivalentTo(new[]
        {
            (Amount: 3_869.34m, Date: "2026-08-27", Superseded: false),
            (Amount: 5_999.00m, Date: "2026-06-24", Superseded: true),
        });
    }

    // ══ The label's rules ═════════════════════════════════════════════════════

    /// <summary>Two credits of ONE person share a label — the label identifies an owner, not a row.</summary>
    [Fact]
    public async Task Two_credits_of_the_same_payee_share_one_label()
    {
        var h = Build(nameof(Two_credits_of_the_same_payee_share_one_label));
        var birgit = AddPayee(h, "Birgit Schneider", "DE-101");

        var tx = AddTransaction(h, "SPLIT-1", birgit.Id);
        AddCredit(h, tx, birgit.Id, 100m, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        AddCredit(h, tx, birgit.Id, 200m, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

        Commissions(await RunAsync(h, "SPLIT-1"))
            .Select(r => r.GetProperty("payeeRef").GetString())
            .Should().AllBe("TransactionPayee");
    }

    /// <summary>Three people, three labels, and only one of them is the sale's own payee.</summary>
    [Fact]
    public async Task Every_further_payee_gets_its_own_numbered_label()
    {
        var h = Build(nameof(Every_further_payee_gets_its_own_numbered_label));
        var owner = AddPayee(h, "Owner", "OWN-1");
        var second = AddPayee(h, "Second", "SEC-1");
        var third = AddPayee(h, "Third", "THI-1");

        var tx = AddTransaction(h, "SPLIT-3", owner.Id);
        AddCredit(h, tx, owner.Id, 100m, new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero));
        AddCredit(h, tx, second.Id, 200m, new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));
        AddCredit(h, tx, third.Id, 300m, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

        Commissions(await RunAsync(h, "SPLIT-3"))
            .Select(r => r.GetProperty("payeeRef").GetString())
            .Should().BeEquivalentTo(["TransactionPayee", "OtherPayee1", "OtherPayee2"]);
    }

    /// <summary>
    /// ★★ NO NAME, NO EMPLOYEE CODE, NO GUID — for the OTHER payee. The label exists to distinguish, not
    /// to identify: docs/Legal.md §3.2 already decided that an opaque reference goes to the provider
    /// INSTEAD of the name, and the DPA is unsigned. If someone "improves" this into a name, this fails.
    ///
    /// The SALE's own payee name is a different matter — it already travelled before this change and is
    /// the subject of the question; that is §3.2's own separate finding, not something this WI widened.
    /// </summary>
    [Fact]
    public async Task The_other_payees_identity_never_reaches_the_payload()
    {
        var h = Build(nameof(The_other_payees_identity_never_reaches_the_payload));
        var birgit = AddPayee(h, "Birgit Schneider", "DE-101");
        var adrian = AddPayee(h, "Adrian Dominguez", "NB-2001");

        var tx = AddTransaction(h, "POL-8554", birgit.Id);
        AddCredit(h, tx, adrian.Id, 5_999m, new DateTimeOffset(2026, 6, 24, 0, 0, 0, TimeSpan.Zero),
            superseded: true);
        AddCredit(h, tx, birgit.Id, 3_869.34m, new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero));

        var json = await h.Tool.RunAsync("""{"reference":"POL-8554"}""", CancellationToken.None);

        json.Should().NotContain("Adrian", "the other payee's name must not reach the provider");
        json.Should().NotContain("NB-2001", "nor their employee code");
        json.Should().NotContain(adrian.Id.ToString(), "nor their id");
    }

    // ══ The order, said out loud ══════════════════════════════════════════════

    /// <summary>
    /// ★ THE ORDER IS DATA. The model was reading meaning into position over a list whose order nobody
    /// had told it — which is how a date from one row ended up beside an amount from another.
    /// </summary>
    [Fact]
    public async Task The_order_of_the_credits_is_stated_in_the_payload()
    {
        var h = Build(nameof(The_order_of_the_credits_is_stated_in_the_payload));
        var birgit = AddPayee(h, "Birgit Schneider", "DE-101");

        var tx = AddTransaction(h, "ORDER-1", birgit.Id);
        AddCredit(h, tx, birgit.Id, 100m, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        AddCredit(h, tx, birgit.Id, 200m, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

        var payload = await RunAsync(h, "ORDER-1");

        payload.GetProperty("commissionsOrderedBy").GetString()
            .Should().Be("MostRecentlyAllocatedFirst");

        // And the rows actually are in that order — a declared order that the data does not follow
        // would be worse than saying nothing.
        Commissions(payload).Select(r => r.GetProperty("creditAllocatedAt").GetString())
            .Should().ContainInOrder("2026-07-01", "2026-06-01");
    }

    /// <summary>A single credit has no order to state, so the field stays out of the payload.</summary>
    [Fact]
    public async Task A_single_credit_carries_no_ordering_claim()
    {
        var h = Build(nameof(A_single_credit_carries_no_ordering_claim));
        var birgit = AddPayee(h, "Birgit Schneider", "DE-101");

        var tx = AddTransaction(h, "ONE-1", birgit.Id);
        AddCredit(h, tx, birgit.Id, 100m, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        var payload = await RunAsync(h, "ONE-1");

        payload.TryGetProperty("commissionsOrderedBy", out _).Should().BeFalse();
        Commissions(payload).Should().ContainSingle()
            .Which.GetProperty("payeeRef").GetString().Should().Be("TransactionPayee");
    }

    /// <summary>
    /// A sale with no payee on it at all (ingestion can leave it null) must not crash and must not
    /// claim any row belongs to the subject — there is no subject.
    /// </summary>
    [Fact]
    public async Task A_sale_with_no_payee_labels_every_credit_as_someone_else()
    {
        var h = Build(nameof(A_sale_with_no_payee_labels_every_credit_as_someone_else));
        var somebody = AddPayee(h, "Somebody", "SOM-1");

        var tx = CompensationTransaction.Ingest(h.TenantId, "ORPHAN-1", payeeId: null,
            Money.Of(1_000m, Eur), new DateOnly(2026, 6, 16), TransactionSource.Manual,
            "seed", Guid.NewGuid(), Now, Guid.NewGuid());
        h.Db.CompensationTransactions.Add(tx);
        h.Db.SaveChanges();

        AddCredit(h, tx, somebody.Id, 40m, new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero));

        Commissions(await RunAsync(h, "ORPHAN-1"))
            .Should().ContainSingle()
            .Which.GetProperty("payeeRef").GetString().Should().Be("OtherPayee1");
    }
}
