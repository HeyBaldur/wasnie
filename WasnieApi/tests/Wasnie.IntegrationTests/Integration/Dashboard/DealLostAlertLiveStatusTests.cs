using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Integrations.Crm;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Dashboard;

/// <summary>
/// The deal-lost alert must report the commission's status RIGHT NOW, not the one it happened to see
/// when the loss was detected.
///
/// The bug these pin shut: the alert stored a snapshot ("Calculated"), finance paid the commission six
/// minutes later, and the dashboard kept saying "you can revert this commission (it has not been paid)"
/// over money that had already left the company. The backend refused the revert — the guard held — but
/// the sentence on screen was false, which for a money screen is its own defect.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class DealLostAlertLiveStatusTests(TestDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    /// <summary>Seeds a CrmSync transaction in <paramref name="liveStatus"/> plus an OPEN deal-lost alert
    /// whose snapshot says <paramref name="statusAtDetection"/> — the two can legitimately differ.</summary>
    private async Task<Guid> SeedAlertAsync(
        string code,
        CompensationTransactionStatus liveStatus,
        CompensationTransactionStatus statusAtDetection,
        bool withChurnDebit = false)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenantId = TestConstants.TenantA;
        var payeeId = Guid.NewGuid();

        var tx = CompensationTransaction.Ingest(
            tenantId, $"HUBSPOT-{code}", payeeId, Money.Of(10_000m, Eur), new DateOnly(2026, 2, 1),
            TransactionSource.CrmSync, "sync", Guid.NewGuid(), Now, Guid.NewGuid(), externalId: code);

        tx.MarkCalculated(1, Money.Of(1_000m, Eur), "sync", Now, Guid.NewGuid());
        if (liveStatus == CompensationTransactionStatus.Paid)
            tx.MarkPaid("sync", Now, Guid.NewGuid());

        db.CompensationTransactions.Add(tx);

        db.DealLostAlerts.Add(DealLostAlert.Create(
            id: Guid.NewGuid(), tenantId: tenantId, source: "HubSpot", externalDealId: code,
            transactionId: tx.Id, referenceNumber: tx.ReferenceNumber,
            transactionStatus: statusAtDetection,          // ← the photo, deliberately possibly stale
            commissionAmount: 1_000m, commissionCurrency: Eur,
            detectedAt: Now.AddMinutes(-6), detectedBy: "hubspot-auto-sync"));

        if (withChurnDebit)
        {
            db.PayeeLedgerEntries.Add(PayeeLedgerEntry.CreateSystemEntry(
                tenantId, payeeId, LedgerTransactionType.ClawbackDebit, Money.Of(500m, Eur),
                "Churn clawback.", LedgerSourceType.DealChurn, "system",
                Guid.NewGuid(), Now, Guid.NewGuid(),
                sourceTransactionId: tx.Id, sourcePlanId: Guid.NewGuid(),
                eventDate: new DateOnly(2026, 5, 2)));
        }

        await db.SaveChangesAsync();
        return tx.Id;
    }

    private async Task<JsonElement> GetAlertAsync(Guid transactionId)
    {
        var client = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.GetAsync("/api/dashboard?period=this-month");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("actionBand").GetProperty("dealLostAlerts")
            .EnumerateArray()
            .Single(a => a.GetProperty("transactionId").GetGuid() == transactionId)
            .Clone();
    }

    [Fact]
    public async Task A_commission_paid_after_detection_reports_as_PAID_not_as_the_stale_snapshot()
    {
        // THE regression. Snapshot says Calculated, the transaction is Paid: the screen must follow the
        // transaction. The snapshot survives as history, which is why both fields are asserted.
        var txId = await SeedAlertAsync(
            "LIVE-PAID",
            liveStatus: CompensationTransactionStatus.Paid,
            statusAtDetection: CompensationTransactionStatus.Calculated);

        var alert = await GetAlertAsync(txId);

        alert.GetProperty("transactionStatus").GetString().Should().Be("Paid",
            "the screen decides on the live status, and this commission has been paid");
        alert.GetProperty("statusAtDetection").GetString().Should().Be("Calculated",
            "the snapshot is kept — it explains why the alert exists");
    }

    [Fact]
    public async Task A_paid_commission_with_no_churn_debit_yet_reads_as_clawback_PENDING()
    {
        var txId = await SeedAlertAsync(
            "LIVE-PENDING",
            liveStatus: CompensationTransactionStatus.Paid,
            statusAtDetection: CompensationTransactionStatus.Paid);

        var alert = await GetAlertAsync(txId);

        alert.GetProperty("transactionStatus").GetString().Should().Be("Paid");
        alert.GetProperty("clawbackState").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task A_paid_commission_whose_debit_is_already_in_the_ledger_reads_as_APPLIED()
    {
        // Answered from the ledger, where the debt actually is — not guessed from the alert.
        var txId = await SeedAlertAsync(
            "LIVE-APPLIED",
            liveStatus: CompensationTransactionStatus.Paid,
            statusAtDetection: CompensationTransactionStatus.Calculated,
            withChurnDebit: true);

        var alert = await GetAlertAsync(txId);

        alert.GetProperty("clawbackState").GetString().Should().Be("Applied");
    }

    [Fact]
    public async Task An_unpaid_commission_still_reports_as_calculated_and_claims_nothing_about_clawback()
    {
        // The control: the revert path must keep working exactly as before. Nothing was clawed back from
        // a commission that was never paid, so the state is NotApplicable rather than "Pending".
        var txId = await SeedAlertAsync(
            "LIVE-CALC",
            liveStatus: CompensationTransactionStatus.Calculated,
            statusAtDetection: CompensationTransactionStatus.Calculated);

        var alert = await GetAlertAsync(txId);

        alert.GetProperty("transactionStatus").GetString().Should().Be("Calculated");
        alert.GetProperty("clawbackState").GetString().Should().Be("NotApplicable");
    }
}
