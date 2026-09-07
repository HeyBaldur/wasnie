using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Handlers.Payouts;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

public sealed class GetPayoutByIdHandlerBuildLinesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PayeeId = Guid.NewGuid();
    private static readonly Guid PlanId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;

    public GetPayoutByIdHandlerBuildLinesTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        tenantCtx.IsResolved.Returns(true);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RuleSnapshot MakeRuleSnapshot(Guid ruleId, Guid planId) =>
        RuleSnapshot.Freeze(ruleId, planId, 1, "Base Commission",
            RateTable.Flat(0.05m), Trigger.Always(), Now);

    private Credit SeedCredit(Guid creditId, Guid txId, Guid ruleId)
    {
        var credit = Credit.Allocate(
            tenantId: TenantId,
            transactionId: txId,
            payeeId: PayeeId,
            planId: PlanId,
            ruleId: ruleId,
            ruleSnapshot: MakeRuleSnapshot(ruleId, PlanId),
            originalAmount: Money.Of(1000m, "EUR"),
            creditedAmount: Money.Of(500m, "EUR"),
            splitPercentage: Percentage.FromPercent(50m),
            role: CreditRole.Primary,
            allocatedBy: "test",
            id: creditId,
            now: Now,
            eventId: Guid.NewGuid());

        _db.Credits.Add(credit);
        return credit;
    }

    private CompensationTransaction SeedTransaction(Guid txId, string reference)
    {
        var tx = CompensationTransaction.Ingest(
            tenantId: TenantId,
            referenceNumber: reference,
            payeeId: PayeeId,
            amount: Money.Of(1000m, "EUR"),
            transactionDate: new DateOnly(2026, 1, 15),
            source: TransactionSource.Manual,
            ingestedBy: "test",
            id: txId,
            now: Now,
            eventId: Guid.NewGuid());

        _db.CompensationTransactions.Add(tx);
        return tx;
    }

    private static CompensationPayout MakePayout(IReadOnlyList<PayoutLineSpec> specs) =>
        CompensationPayout.Calculate(
            tenantId: TenantId,
            payeeId: PayeeId,
            planId: PlanId,
            payeeSnapshot: PayeeReference.Snapshot(PayeeId, "Ada Kowalski", "EMP301"),
            period: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            lineSpecs: specs,
            fallbackCurrency: "EUR",
            calculatedBy: "test",
            id: Guid.NewGuid(),
            now: Now,
            eventId: Guid.NewGuid(),
            newId: () => Guid.NewGuid());

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildLinesAsync_PopulatesTransactionReference()
    {
        var creditId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        SeedCredit(creditId, txId, ruleId);
        SeedTransaction(txId, "POL-87765312");
        await _db.SaveChangesAsync();

        var spec = new PayoutLineSpec(
            CreditId: creditId,
            RuleId: ruleId,
            RuleName: "Base Commission",
            BaseAmount: Money.Of(500m, "EUR"),
            CommissionAmount: Money.Of(25m, "EUR"),
            AppliedModifiers: []);

        var payout = MakePayout([spec]);
        var result = await GetPayoutByIdHandler.BuildLinesAsync(payout.Lines, _db, payout.Id, default);

        result.Should().HaveCount(1);
        result[0].TransactionReference.Should().Be("POL-87765312");
        result[0].TransactionId.Should().Be(txId);
        result[0].TransactionDate.Should().Be(new DateOnly(2026, 1, 15));
        result[0].TransactionAmount.Should().Be(1000m);
        result[0].TransactionCurrency.Should().Be("EUR");
    }

    [Fact]
    public async Task BuildLinesAsync_PreservesRuleAndAmountFields()
    {
        var creditId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        SeedCredit(creditId, txId, ruleId);
        SeedTransaction(txId, "POL-00001");
        await _db.SaveChangesAsync();

        var spec = new PayoutLineSpec(
            CreditId: creditId,
            RuleId: ruleId,
            RuleName: "Tier-2 Bonus",
            BaseAmount: Money.Of(500m, "EUR"),
            CommissionAmount: Money.Of(75m, "EUR"),
            AppliedModifiers: []);

        var payout = MakePayout([spec]);
        var result = await GetPayoutByIdHandler.BuildLinesAsync(payout.Lines, _db, payout.Id, default);

        var line = result[0];
        line.RuleName.Should().Be("Tier-2 Bonus");
        line.BaseAmount.Should().Be(500m);
        line.BaseCurrency.Should().Be("EUR");
        line.CommissionAmount.Should().Be(75m);
        line.CommissionCurrency.Should().Be("EUR");
        line.CreditId.Should().Be(creditId);
    }

    [Fact]
    public async Task BuildLinesAsync_MissingCredit_NullsTransactionFields()
    {
        // CreditId not in DB — simulates data gap, must not throw
        var missingCreditId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        var spec = new PayoutLineSpec(
            CreditId: missingCreditId,
            RuleId: ruleId,
            RuleName: "Base Commission",
            BaseAmount: Money.Of(500m, "EUR"),
            CommissionAmount: Money.Of(25m, "EUR"),
            AppliedModifiers: []);

        var payout = MakePayout([spec]);
        var result = await GetPayoutByIdHandler.BuildLinesAsync(payout.Lines, _db, payout.Id, default);

        result.Should().HaveCount(1);
        result[0].TransactionId.Should().BeNull();
        result[0].TransactionReference.Should().BeNull();
        result[0].TransactionDate.Should().BeNull();
        result[0].TransactionAmount.Should().BeNull();
    }

    [Fact]
    public async Task BuildLinesAsync_MultipleLines_NoN1_AllResolved()
    {
        var ruleId = Guid.NewGuid();
        var specs = new List<PayoutLineSpec>();
        for (var i = 0; i < 3; i++)
        {
            var creditId = Guid.NewGuid();
            var txId = Guid.NewGuid();
            SeedCredit(creditId, txId, ruleId);
            SeedTransaction(txId, $"POL-{i + 1:D5}");
            specs.Add(new PayoutLineSpec(
                CreditId: creditId,
                RuleId: ruleId,
                RuleName: "Base Commission",
                BaseAmount: Money.Of(100m, "EUR"),
                CommissionAmount: Money.Of(10m, "EUR"),
                AppliedModifiers: []));
        }
        await _db.SaveChangesAsync();

        var payout = MakePayout(specs);
        var result = await GetPayoutByIdHandler.BuildLinesAsync(payout.Lines, _db, payout.Id, default);

        result.Should().HaveCount(3);
        result.Select(l => l.TransactionReference).Should().NotContainNulls();
        result.Select(l => l.TransactionReference).Should().OnlyHaveUniqueItems();
    }
}
