using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Compensation.Commands.Ledger;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Ledger;

/// <summary>
/// End to end, through the RUNNING APPLICATION: the churn trigger fires inside the app's own MediatR
/// pipeline (resolved from the host's container, not hand-constructed), and the resulting debt is then
/// read back over HTTP exactly as the payee's statement screen reads it.
///
/// The trigger has no HTTP endpoint by design — it is fired by the CRM reverse reconciler, not by a
/// person — so "empirical" here means the real pipeline plus the real read surface, which is every layer
/// a user actually touches.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class ChurnClawbackEndToEndTests(TestDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ClosedWonOn = new(2026, 1, 10);
    private const string Eur = "EUR";

    private sealed record Seed(Guid PayeeId, Guid PlanId, Guid TxId);

    /// <summary>A payee with a PAID commission of 1,000 under a plan with a 90-day maturation window.</summary>
    private async Task<Seed> SeedPaidCommissionAsync(string code)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenantId = TestConstants.TenantA;

        var payee = Payee.Create(tenantId, $"Payee {code}", code, $"{code}@test.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        db.Payees.Add(payee);

        var plan = Plan.Create(tenantId, $"Plan {code}", "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), Eur,
            "test", Guid.NewGuid(), Now, Guid.NewGuid());
        plan.AddRule("Commission", 1,
            new Measurement
            {
                Type = MeasurementType.Revenue,
                SourceField = "amount",
                Aggregation = MeasurementAggregation.Sum,
            },
            RateTable.Flat(0.10m));
        plan.SetClawbackPolicy(90, 100m, "test", Now);
        db.CompensationPlans.Add(plan);
        await db.SaveChangesAsync();

        var ruleId = plan.Rules.First().Id;
        var snapshot = RuleSnapshot.Freeze(ruleId, plan.Id, 1, "Commission",
            RateTable.Flat(0.10m), Trigger.Always(), Now);

        var tx = CompensationTransaction.Ingest(
            tenantId, $"HUBSPOT-{code}", payee.Id, Money.Of(10_000m, Eur), ClosedWonOn,
            TransactionSource.CrmSync, "sync", Guid.NewGuid(), Now, Guid.NewGuid(),
            externalId: $"9100-{code}");
        db.CompensationTransactions.Add(tx);

        var commission = Money.Of(1_000m, Eur);
        var credit = Credit.Allocate(
            tenantId, tx.Id, payee.Id, plan.Id, ruleId, snapshot, Money.Of(10_000m, Eur), commission,
            Percentage.FromPercent(100), CreditRole.Primary, "sync", Guid.NewGuid(), Now, Guid.NewGuid());
        db.Credits.Add(credit);

        tx.MarkCalculated(1, commission, "sync", Now, Guid.NewGuid());
        tx.MarkPaid("sync", Now, Guid.NewGuid());
        credit.Consume(Guid.NewGuid(), Now, Guid.NewGuid());
        await db.SaveChangesAsync();

        return new Seed(payee.Id, plan.Id, tx.Id);
    }

    [Fact]
    public async Task A_churned_deal_becomes_a_debt_the_payee_can_see_on_their_statement()
    {
        var seed = await SeedPaidCommissionAsync("E2E-CHURN");
        var lostOn = ClosedWonOn.AddDays(30); // 1000 × 60 / 90 = 666.6667

        // ── The trigger, through the application's own pipeline ──────────────
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(new RegisterDealChurnClawbackCommand(
                TestConstants.TenantA, seed.TxId, lostOn, "9100"));

            result.IsSuccess.Should().BeTrue();
            result.Value!.Entries.Single().Amount.Should().Be(-666.6667m);
        }

        // ── The read surface: what a person actually sees ────────────────────
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        var entriesResponse = await client.GetAsync($"/api/payees/{seed.PayeeId}/ledger/entries");
        entriesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = JsonDocument.Parse(await entriesResponse.Content.ReadAsStringAsync())
            .RootElement.EnumerateArray().ToList();

        entries.Should().HaveCount(1);
        var row = entries[0];
        row.GetProperty("transactionType").GetString().Should().Be("ClawbackDebit");
        row.GetProperty("origin").GetString().Should().Be("System");
        row.GetProperty("amount").GetDecimal().Should().Be(-666.6667m);
        row.GetProperty("daysActive").GetInt32().Should().Be(30);
        row.GetProperty("maturationDays").GetInt32().Should().Be(90);
        row.GetProperty("sourceExternalDealId").GetString().Should().Be("9100");
        // The justification is what the rep reads to understand a number that reduced their pay.
        row.GetProperty("justification").GetString()
            .Should().Contain("2026-02-09").And.Contain("30 of 90 maturation days");

        var statementResponse = await client.GetAsync($"/api/payees/{seed.PayeeId}/ledger/statement");
        statementResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var statements = JsonDocument.Parse(await statementResponse.Content.ReadAsStringAsync())
            .RootElement.EnumerateArray().ToList();

        var statement = statements.Single(s => s.GetProperty("currency").GetString() == Eur);
        statement.GetProperty("newCarryover").GetDecimal().Should().Be(-666.6667m,
            "the debt is what the payee carries into the next run");

        // ── The structured fields (WI-UI-CLEANUP) ────────────────────────────
        // The loss date and the originating plan travel as TYPED fields, so the table renders them in
        // their own column and in the reader's locale instead of parsing them out of the sentence.
        row.GetProperty("eventDate").GetString().Should().Be("2026-02-09",
            "the CRM loss date is a field, not a phrase inside the justification");
        row.GetProperty("sourcePlanId").GetGuid().Should().Be(seed.PlanId);

        // The booking date is TODAY's, not the CRM event date — the separation, visible in the payload.
        var createdAt = row.GetProperty("createdAt").GetDateTimeOffset();
        createdAt.UtcDateTime.Date.Should().BeAfter(lostOn.ToDateTime(TimeOnly.MinValue));

        // And nothing about the payment itself was rewritten.
        using var verifyScope = fixture.Factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tx = await db.CompensationTransactions.IgnoreQueryFilters().SingleAsync(t => t.Id == seed.TxId);
        tx.Status.Should().Be(CompensationTransactionStatus.Paid);
        (await db.Credits.IgnoreQueryFilters().SingleAsync(c => c.TransactionId == seed.TxId))
            .SupersededAt.Should().BeNull();
    }

    [Fact]
    public async Task A_rep_sees_their_own_churn_debt_and_the_reason_for_it()
    {
        // Transparency is the product decision: the person whose pay shrank can read why.
        var seed = await SeedPaidCommissionAsync("E2E-REP");

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new RegisterDealChurnClawbackCommand(
                    TestConstants.TenantA, seed.TxId, ClosedWonOn.AddDays(45), "9100"));
        }

        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Rep");

        var response = await client.GetAsync($"/api/payees/{seed.PayeeId}/ledger/statement");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var statement = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.EnumerateArray().Single(s => s.GetProperty("currency").GetString() == Eur);
        statement.GetProperty("newCarryover").GetDecimal().Should().Be(-500m); // 1000 × 45 / 90
    }
}
