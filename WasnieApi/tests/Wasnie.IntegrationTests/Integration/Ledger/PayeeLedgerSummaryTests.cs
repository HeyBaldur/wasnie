using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Ledger;

/// <summary>
/// The crossing of earnings and debt, against real SQL Server.
///
/// ★★ TEST 1 IS THE WHOLE WORK ITEM. A payee who earned 10,000 and owes nothing must come back as
/// "earned 10,000, debt 0, net 10,000" carrying the EarningsAndNoDebt token — never as the ledger's bare
/// 0.00, which is what every previous route to this question would have produced.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PayeeLedgerSummaryTests(TestDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    private async Task<Guid> SeedPayeeAsync(string code, string? ownerUserId = null, Guid? managerId = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var payee = Payee.Create(TestConstants.TenantA, $"Payee {code}", code,
            $"{code}@test.com".ToLowerInvariant(), new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        if (ownerUserId is not null) payee.LinkToUser(ownerUserId, "test", Now);
        if (managerId is not null) payee.AssignManager(managerId.Value, "test", Now);
        db.Payees.Add(payee);
        await db.SaveChangesAsync();
        return payee.Id;
    }

    /// <summary>
    /// A payout for the given amount, in the given state — built through the real factory with a real
    /// line, so <c>TotalCommission</c> is computed by the domain exactly as the engine computes it.
    /// </summary>
    private async Task SeedPayoutAsync(
        Guid payeeId, decimal amount, CompensationPayoutStatus status,
        DateOnly? periodStart = null, DateOnly? periodEnd = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // No READ in the seed: a query here would evaluate the tenant filter outside a request, where
        // there is no tenant to read. The payout's payee SNAPSHOT is not what this suite measures — the
        // name in the answer comes from the Payee row — so a fixed snapshot is enough.
        var spec = new PayoutLineSpec(
            CreditId: Guid.NewGuid(),
            RuleId: Guid.NewGuid(),
            RuleName: "Base",
            BaseAmount: Money.Of(amount * 10m, Eur),
            CommissionAmount: Money.Of(amount, Eur),
            AppliedModifiers: []);

        var payout = CompensationPayout.Calculate(
            TestConstants.TenantA, payeeId, Guid.NewGuid(),
            PayeeReference.Snapshot(payeeId, "Seeded Payee", "EMP-SEED"),
            DateRange.Of(periodStart ?? new DateOnly(2026, 1, 1), periodEnd ?? new DateOnly(2026, 12, 31)),
            [spec], Eur, "test", Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);

        if (status is CompensationPayoutStatus.Approved or CompensationPayoutStatus.Paid)
            payout.Approve("test", Now, Guid.NewGuid());
        if (status == CompensationPayoutStatus.Paid)
            payout.MarkPaid("test", Now);
        if (status == CompensationPayoutStatus.Disputed)
            payout.Dispute("test", Now);

        db.CompensationPayouts.Add(payout);
        await db.SaveChangesAsync();
    }

    private async Task SeedDebtAsync(Guid payeeId, decimal debt)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var entry = PayeeLedgerEntry.CreateSystemEntry(
            TestConstants.TenantA, payeeId, LedgerTransactionType.ClawbackDebit,
            Money.Of(debt, Eur), "Deal churned.", LedgerSourceType.DealChurn,
            "system", Guid.NewGuid(), Now, Guid.NewGuid());
        var balance = PayeeBalance.Open(TestConstants.TenantA, payeeId, Eur, Guid.NewGuid(), Now);
        balance.Apply(entry, Now);

        db.PayeeLedgerEntries.Add(entry);
        db.PayeeBalances.Add(balance);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Asks over HTTP, with a real token. Deliberately not through ISender: the permissions and the
    /// resource guard read the CLAIMS, so a query sent from a bare scope would be authorised as nobody
    /// and prove nothing about who may ask.
    /// </summary>
    private Task<HttpResponseMessage> AskAsync(
        Guid payeeId, string role, string userId, string period = "all-time") =>
        fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, userId, role)
            .GetAsync($"/api/payees/{payeeId}/ledger/summary?period={period}");

    private static async Task<CurrencyRow> EurRowAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = JsonSerializer.Deserialize<SummaryResponse>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() },
            });

        return summary!.ByCurrency.Single(c => c.Currency == Eur);
    }

    private sealed record SummaryResponse(
        Guid PayeeId, string PayeeName, string PeriodLabel, IReadOnlyList<CurrencyRow> ByCurrency);

    private sealed record CurrencyRow(
        string Currency,
        decimal EarnedCommissionsInPeriod,
        decimal PaidOutInPeriod,
        decimal DisputedInPeriod,
        decimal AwaitingPaymentAllTime,
        decimal OutstandingDebt,
        decimal NetPendingPayout,
        BalanceSemantic Interpretation);

    // ══ 1. THE FALSE ZERO ════════════════════════════════════════════════════

    [Fact]
    public async Task Earnings_with_no_debt_report_the_earnings_not_a_zero_balance()
    {
        var payeeId = await SeedPayeeAsync($"SUM-FALSEZERO-{Guid.NewGuid():N}"[..14]);
        await SeedPayoutAsync(payeeId, 10_000m, CompensationPayoutStatus.Approved);

        var eur = await EurRowAsync(await AskAsync(payeeId, "CompManager", "user-finance"));

        eur.EarnedCommissionsInPeriod.Should().Be(10_000m);
        eur.OutstandingDebt.Should().Be(0m, "the ledger only records debt, and there is none");
        eur.AwaitingPaymentAllTime.Should().Be(10_000m);
        eur.NetPendingPayout.Should().Be(10_000m, "★ this is the number the false zero used to hide");
        eur.Interpretation.Should().Be(BalanceSemantic.EarningsAndNoDebt);
    }

    // ══ 2. Earnings crossed with a real debt ═════════════════════════════════

    [Fact]
    public async Task A_clawback_is_subtracted_from_what_is_pending()
    {
        var payeeId = await SeedPayeeAsync($"SUM-DEBT-{Guid.NewGuid():N}"[..14]);
        await SeedPayoutAsync(payeeId, 10_000m, CompensationPayoutStatus.Approved);
        await SeedDebtAsync(payeeId, 2_500m);

        var eur = await EurRowAsync(await AskAsync(payeeId, "CompManager", "user-finance"));

        eur.EarnedCommissionsInPeriod.Should().Be(10_000m);
        eur.OutstandingDebt.Should().Be(2_500m, "stored negative, reported as a positive magnitude");
        eur.NetPendingPayout.Should().Be(7_500m);
        eur.Interpretation.Should().Be(BalanceSemantic.EarningsWithDebt);
    }

    [Fact]
    public async Task A_debt_larger_than_everything_pending_is_flagged_as_carrying_over()
    {
        var payeeId = await SeedPayeeAsync($"SUM-OVER-{Guid.NewGuid():N}"[..14]);
        await SeedPayoutAsync(payeeId, 1_000m, CompensationPayoutStatus.Calculated);
        await SeedDebtAsync(payeeId, 4_000m);

        var eur = await EurRowAsync(await AskAsync(payeeId, "CompManager", "user-finance"));

        eur.NetPendingPayout.Should().Be(-3_000m);
        eur.Interpretation.Should().Be(BalanceSemantic.DebtExceedsPending);
    }

    [Fact]
    public async Task A_debt_with_nothing_pending_is_debt_only()
    {
        var payeeId = await SeedPayeeAsync($"SUM-DONLY-{Guid.NewGuid():N}"[..14]);
        await SeedDebtAsync(payeeId, 800m);

        var eur = await EurRowAsync(await AskAsync(payeeId, "CompManager", "user-finance"));

        eur.Interpretation.Should().Be(BalanceSemantic.DebtOnly);
        eur.NetPendingPayout.Should().Be(-800m);
    }

    // ══ 3. Cash vs accrual vs disputed ═══════════════════════════════════════

    /// <summary>
    /// A paid payout is earned AND paid out, and it is NOT awaiting payment. Getting this wrong would
    /// promise a payee money that already reached their bank.
    /// </summary>
    [Fact]
    public async Task Paid_money_counts_as_earned_and_as_cash_but_never_as_pending()
    {
        var payeeId = await SeedPayeeAsync($"SUM-CASH-{Guid.NewGuid():N}"[..14]);
        await SeedPayoutAsync(payeeId, 3_000m, CompensationPayoutStatus.Paid);
        await SeedPayoutAsync(payeeId, 500m, CompensationPayoutStatus.Disputed);

        var eur = await EurRowAsync(await AskAsync(payeeId, "CompManager", "user-finance"));

        eur.EarnedCommissionsInPeriod.Should().Be(3_000m, "disputed money is excluded from earned");
        eur.PaidOutInPeriod.Should().Be(3_000m);
        eur.AwaitingPaymentAllTime.Should().Be(0m, "it has already been paid");
        eur.DisputedInPeriod.Should().Be(500m, "reported on its own rather than dropped");
        eur.NetPendingPayout.Should().Be(0m);
    }

    // ══ 4. Isolation ═════════════════════════════════════════════════════════
    //
    // The old "a rep is refused because they lack Payouts.Read" test lived here and has been REPLACED,
    // not deleted: a rep now holds LedgerSummary.Read, so the permission no longer does the refusing —
    // the resource guard does, and A_rep_with_the_permission_still_cannot_summarise_a_colleague below is
    // the same protection asserted against the layer that now provides it.

    // ══ THE FACADE ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ THE POINT OF THE FACADE, IN ONE TEST. A Rep holds LedgerSummary.Read and NOT Payouts.Read, and
    /// gets their own crossed balance anyway. Before the facade this exact call was a 403: the summary
    /// demanded Payouts.Read, so the roles the false zero hurts most were the ones locked out of the fix.
    /// </summary>
    [Fact]
    public async Task A_rep_without_payouts_read_still_receives_their_own_crossed_balance()
    {
        var repUser = $"user-{Guid.NewGuid():N}";
        var ownId = await SeedPayeeAsync($"SUM-OWN-{Guid.NewGuid():N}"[..14], repUser);
        await SeedPayoutAsync(ownId, 4_000m, CompensationPayoutStatus.Approved);
        await SeedDebtAsync(ownId, 1_000m);

        var eur = await EurRowAsync(await AskAsync(ownId, "Rep", repUser));

        eur.EarnedCommissionsInPeriod.Should().Be(4_000m, "the earned half comes from payouts the rep " +
            "may not read directly — that is exactly what the facade is for");
        eur.OutstandingDebt.Should().Be(1_000m);
        eur.NetPendingPayout.Should().Be(3_000m);
        eur.Interpretation.Should().Be(BalanceSemantic.EarningsWithDebt);
    }

    /// <summary>
    /// ★ AND THE PAYROLL SURFACE STAYS SHUT. The facade would be worthless if it leaked the permission
    /// it was built to avoid granting: the same rep, in the same session, must still bounce off every
    /// raw payouts and pay-run endpoint. Deleting LedgerSummaryRead from RepPermissions turns the test
    /// above red; adding PayoutsRead to it turns THIS one red.
    /// </summary>
    [Fact]
    public async Task The_same_rep_is_still_locked_out_of_the_raw_payout_endpoints()
    {
        var repUser = $"user-{Guid.NewGuid():N}";
        var ownId = await SeedPayeeAsync($"SUM-RAW-{Guid.NewGuid():N}"[..14], repUser);
        await SeedPayoutAsync(ownId, 4_000m, CompensationPayoutStatus.Approved);

        var rep = fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, repUser, "Rep");

        (await rep.GetAsync("/api/payouts?page=1&pageSize=10")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "raw payout rows are payroll's, not the sales floor's");
        (await rep.GetAsync($"/api/payouts/{Guid.NewGuid()}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await rep.GetAsync("/api/pay-runs?page=1&pageSize=10")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_manager_receives_their_direct_reports_balance_without_payouts_read()
    {
        var managerUser = $"user-{Guid.NewGuid():N}";
        var managerPayeeId = await SeedPayeeAsync($"SUM-MGR-{Guid.NewGuid():N}"[..14], managerUser);
        var reportId = await SeedPayeeAsync(
            $"SUM-REP-{Guid.NewGuid():N}"[..14], $"user-{Guid.NewGuid():N}", managerPayeeId);
        await SeedPayoutAsync(reportId, 7_000m, CompensationPayoutStatus.Approved);

        var eur = await EurRowAsync(await AskAsync(reportId, "Manager", managerUser));

        eur.NetPendingPayout.Should().Be(7_000m,
            "explaining a reduced payment needs the earned half, which is what the facade grants");
    }

    /// <summary>
    /// The permission answers WHAT you may receive; the guard answers WHOSE. Now that a Rep holds the
    /// permission, the guard is the only thing between them and a colleague's balance.
    /// </summary>
    [Fact]
    public async Task A_rep_with_the_permission_still_cannot_summarise_a_colleague()
    {
        var repUser = $"user-{Guid.NewGuid():N}";
        await SeedPayeeAsync($"SUM-ME-{Guid.NewGuid():N}"[..14], repUser);
        var colleagueId = await SeedPayeeAsync(
            $"SUM-THEM-{Guid.NewGuid():N}"[..14], $"user-{Guid.NewGuid():N}");
        await SeedPayoutAsync(colleagueId, 50_000m, CompensationPayoutStatus.Approved);

        var response = await AskAsync(colleagueId, "Rep", repUser);

        // The SAME refusal an unknown id produces — the permission must not make the guard legible.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Payee not found.");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("50000");
    }

    [Fact]
    public async Task An_unlinked_rep_gets_nothing_from_the_facade()
    {
        var ownId = await SeedPayeeAsync($"SUM-UNLK-{Guid.NewGuid():N}"[..14]);
        await SeedPayoutAsync(ownId, 3_000m, CompensationPayoutStatus.Approved);

        // Fail-closed survives the new permission: holding LedgerSummary.Read with no payee of your own
        // resolves to a visibility of nothing, not to everything.
        (await AskAsync(ownId, "Rep", $"user-{Guid.NewGuid():N}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// An id that does not exist and an id that is out of reach must answer identically — same status,
    /// same body — or the endpoint maps out which payees are real.
    /// </summary>
    [Fact]
    public async Task An_unknown_payee_answers_with_the_shared_refusal()
    {
        var missing = await AskAsync(Guid.NewGuid(), "CompManager", "user-finance");

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).Should().Contain("Payee not found.");
    }

    // ══ 5. Period scoping ════════════════════════════════════════════════════

    /// <summary>
    /// Earnings are period-scoped; the debt is NOT, because the ledger has no period dimension. A
    /// narrow window must therefore drop the earnings and keep the debt — the asymmetry is the design.
    /// </summary>
    [Fact]
    public async Task The_window_filters_earnings_but_never_the_debt()
    {
        var payeeId = await SeedPayeeAsync($"SUM-PERIOD-{Guid.NewGuid():N}"[..14]);
        await SeedPayoutAsync(payeeId, 6_000m, CompensationPayoutStatus.Approved,
            new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31));
        await SeedDebtAsync(payeeId, 1_000m);

        var eur = await EurRowAsync(await AskAsync(payeeId, "CompManager", "user-finance", "this-month"));

        eur.EarnedCommissionsInPeriod.Should().Be(0m, "the 2020 payout is outside this month");
        eur.OutstandingDebt.Should().Be(1_000m, "the debt is as of now, never window-scoped");
        eur.AwaitingPaymentAllTime.Should().Be(6_000m, "still unpaid, whatever period earned it");
    }
}
