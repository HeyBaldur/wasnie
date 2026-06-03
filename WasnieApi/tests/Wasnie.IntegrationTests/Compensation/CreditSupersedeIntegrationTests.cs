using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Compensation;

/// <summary>
/// Integration tests for Decision #46 Case A: Credit superseding when a Calculated transaction is reassigned.
/// Uses a real Testcontainers SQL Server via the shared WasnieIntegrationTestCollection.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class CreditSupersedeIntegrationTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _admin = null!;

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private const string Currency = "EUR";

    public CreditSupersedeIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetCreditSupersedeTestDataAsync();
        _admin = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<Guid> CreatePayeeAsync(string code)
    {
        var resp = await _admin.PostAsJsonAsync("/api/payees", new
        {
            fullName = $"Test {code}",
            employeeCode = code,
            email = $"{code.ToLower().Replace("-", "")}@supersede.test",
            hireDate = "2022-01-01"
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<IdResponse>();
        return body!.Id;
    }

    private async Task<TxResponse> PostTransactionAsync(Guid payeeId, string refNum, decimal amount = 1000m)
    {
        var resp = await _admin.PostAsJsonAsync("/api/transactions", new
        {
            referenceNumber = refNum,
            payeeId,
            amount,
            currency = Currency,
            transactionDate = "2025-06-15"
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TxResponse>())!;
    }

    private async Task<TxResponse> ReassignAsync(Guid txId, Guid newPayeeId, string reason)
    {
        var resp = await _admin.PostAsJsonAsync($"/api/transactions/{txId}/reassign-payee", new
        {
            newPayeeId,
            reason
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TxResponse>())!;
    }

    // Sets up Plan + PlanAssignment for the given payee covering 2025-06-15.
    private async Task SeedPlanAssignmentAsync(Guid payeeId, string planName)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var planId = Guid.NewGuid();
        var plan = Plan.Create(TestConstants.TenantA, planName, "desc",
            DateRange.Of(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
            Currency, "test", planId, now, Guid.NewGuid());
        plan.AddRule("10pct Commission", 1,
            new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
            RateTable.Flat(0.10m));
        db.CompensationPlans.Add(plan);

        var payeeRef = PayeeReference.Snapshot(payeeId, "Test", "code");
        db.PlanAssignments.Add(PlanAssignment.Create(
            TestConstants.TenantA, planId, payeeId, payeeRef,
            DateRange.Of(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
            "test", Guid.NewGuid(), now, Guid.NewGuid()));

        await db.SaveChangesAsync();
    }

    private ApplicationDbContext GetVerifyDb()
    {
        var scope = _fixture.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reassign_CalculatedTransaction_NewPayeeHasPlan_OldCreditSuperseded_NewCreditCreated()
    {
        // Arrange: payeeA with plan → transaction becomes Calculated.
        var payeeAId = await CreatePayeeAsync("EMP-CS-A01");
        var payeeBId = await CreatePayeeAsync("EMP-CS-B01");
        await SeedPlanAssignmentAsync(payeeAId, "Plan-CS-A01");
        await SeedPlanAssignmentAsync(payeeBId, "Plan-CS-B01");

        var tx = await PostTransactionAsync(payeeAId, "REF-CS-001");
        tx.Status.Should().Be("Calculated", "transaction should be Calculated after ingest with a plan");

        // Verify Credit was created for payeeA.
        using (var db = GetVerifyDb())
        {
            var creditsA = await db.Credits.IgnoreQueryFilters()
                .Where(c => c.TenantId == TestConstants.TenantA && c.TransactionId == tx.Id)
                .ToListAsync();
            creditsA.Should().HaveCount(1);
            creditsA[0].PayeeId.Should().Be(payeeAId);
            creditsA[0].SupersededAt.Should().BeNull("Credit should be active before reassign");
        }

        // Act: reassign to payeeB.
        var reassigned = await ReassignAsync(tx.Id, payeeBId, "Supervisor confirmed correct rep at shift close");

        // Assert response.
        reassigned.PayeeId.Should().Be(payeeBId);
        reassigned.Status.Should().Be("Calculated", "new payee has a plan so transaction should be re-Calculated");

        // Assert DB state.
        using var verifyDb = GetVerifyDb();
        var allCredits = await verifyDb.Credits.IgnoreQueryFilters()
            .Where(c => c.TenantId == TestConstants.TenantA && c.TransactionId == tx.Id)
            .ToListAsync();

        allCredits.Should().HaveCount(2, "one superseded for payeeA, one active for payeeB");

        var oldCredit = allCredits.Single(c => c.PayeeId == payeeAId);
        oldCredit.SupersededAt.Should().NotBeNull("old Credit must be superseded");
        oldCredit.SupersededBy.Should().Contain(payeeAId.ToString());
        oldCredit.SupersededBy.Should().Contain(payeeBId.ToString());

        var newCredit = allCredits.Single(c => c.PayeeId == payeeBId);
        newCredit.SupersededAt.Should().BeNull("new Credit must be active");
        newCredit.CreditedAmount.Amount.Should().Be(100m); // 1000 * 10%
    }

    [Fact]
    public async Task Reassign_CalculatedTransaction_NewPayeeHasNoPlan_OldCreditSuperseded_TransactionPending()
    {
        // Arrange: payeeA with plan, payeeB without plan.
        var payeeAId = await CreatePayeeAsync("EMP-CS-A02");
        var payeeBId = await CreatePayeeAsync("EMP-CS-B02");
        await SeedPlanAssignmentAsync(payeeAId, "Plan-CS-A02");
        // payeeB has NO plan assignment

        var tx = await PostTransactionAsync(payeeAId, "REF-CS-002");
        tx.Status.Should().Be("Calculated");

        // Act.
        var reassigned = await ReassignAsync(tx.Id, payeeBId, "Manager error in original assignment override");

        // Assert.
        reassigned.PayeeId.Should().Be(payeeBId);
        reassigned.Status.Should().Be("Pending", "new payee has no plan so transaction stays Pending");

        using var verifyDb = GetVerifyDb();
        var allCredits = await verifyDb.Credits.IgnoreQueryFilters()
            .Where(c => c.TenantId == TestConstants.TenantA && c.TransactionId == tx.Id)
            .ToListAsync();

        allCredits.Should().HaveCount(1, "only the original superseded Credit");
        allCredits[0].PayeeId.Should().Be(payeeAId);
        allCredits[0].SupersededAt.Should().NotBeNull("original Credit must be superseded");

        // No new Credits for payeeB.
        allCredits.Should().NotContain(c => c.PayeeId == payeeBId);
    }

    [Fact]
    public async Task Reassign_PendingTransaction_NoCreditsToSupersede_StillReassigns()
    {
        // Arrange: no plan → transaction stays Pending → no Credits.
        var payeeAId = await CreatePayeeAsync("EMP-CS-A03");
        var payeeBId = await CreatePayeeAsync("EMP-CS-B03");
        // Neither payee has a plan assignment.

        var tx = await PostTransactionAsync(payeeAId, "REF-CS-003");
        tx.Status.Should().Be("Pending");

        // Act.
        var reassigned = await ReassignAsync(tx.Id, payeeBId, "Correcting initial assignment mistake here");

        // Assert: normal reassign worked, no Credits exist.
        reassigned.PayeeId.Should().Be(payeeBId);

        using var verifyDb = GetVerifyDb();
        var credits = await verifyDb.Credits.IgnoreQueryFilters()
            .Where(c => c.TenantId == TestConstants.TenantA && c.TransactionId == tx.Id)
            .ToListAsync();
        credits.Should().BeEmpty("no Credits were ever created");
    }

    [Fact]
    public async Task Reassign_CalculatedTransaction_SupersedeReasonContainsStructuredInfo()
    {
        var payeeAId = await CreatePayeeAsync("EMP-CS-A04");
        var payeeBId = await CreatePayeeAsync("EMP-CS-B04");
        await SeedPlanAssignmentAsync(payeeAId, "Plan-CS-A04");

        var tx = await PostTransactionAsync(payeeAId, "REF-CS-004");
        var commandReason = "System corrected assignment due to scheduling conflict";
        await ReassignAsync(tx.Id, payeeBId, commandReason);

        using var verifyDb = GetVerifyDb();
        var oldCredit = await verifyDb.Credits.IgnoreQueryFilters()
            .Where(c => c.TenantId == TestConstants.TenantA && c.TransactionId == tx.Id && c.PayeeId == payeeAId)
            .SingleAsync();

        oldCredit.SupersededBy.Should().NotBeNull();
        oldCredit.SupersededBy!.Length.Should().BeLessOrEqualTo(500);
        oldCredit.SupersededBy.Should().Contain(payeeAId.ToString());
        oldCredit.SupersededBy.Should().Contain(payeeBId.ToString());
    }

    private sealed record IdResponse(Guid Id);
    private sealed record TxResponse(Guid Id, Guid? PayeeId, string Status);
}
