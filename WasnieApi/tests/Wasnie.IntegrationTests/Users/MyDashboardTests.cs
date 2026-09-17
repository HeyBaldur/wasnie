using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Users;

/// <summary>
/// The personal dashboard (KAN-92, batch 3), against real SQL Server.
///
/// ★★ TEST 3 IS THE SECURITY PROPERTY AND THE REASON THE ROUTE TAKES NO IDENTIFIER. Two reps ask the
/// same URL with the same shape of request and get different answers, because the payee is resolved
/// from the token. There is nothing in the request to tamper with.
///
/// ★★ TEST 2 IS THE FALSE ZERO. A login with no payee attached must come back saying so, not as a
/// page of 0.00 — the two are opposite news for the person reading them.
///
/// ★ IT ASKS OVER HTTP, not through ISender: the payee is read from the CLAIMS, so a query sent from a
/// bare scope would be answered for nobody and would prove nothing about whose figures come back.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class MyDashboardTests(TestDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    private async Task<Guid> SeedPayeeAsync(string code, string? ownerUserId = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var payee = Payee.Create(TestConstants.TenantA, $"Payee {code}", code,
            $"{code}@test.com".ToLowerInvariant(), new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        if (ownerUserId is not null) payee.LinkToUser(ownerUserId, "test", Now);
        db.Payees.Add(payee);
        await db.SaveChangesAsync();
        return payee.Id;
    }

    private async Task SeedPayoutAsync(Guid payeeId, decimal amount)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

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
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            [spec], Eur, "test", Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);

        payout.Approve("test", Now, Guid.NewGuid());

        db.CompensationPayouts.Add(payout);
        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> AskAsync(string userId, string role = "Rep") =>
        fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, userId, role)
            .GetAsync("/api/me/dashboard");

    private static async Task<DashboardResponse> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonSerializer.Deserialize<DashboardResponse>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() },
            })!;
    }

    private sealed record DashboardResponse(
        bool Linked, Guid? PayeeId, string? PayeeName,
        SummaryResponse? Summary, IReadOnlyList<QuotaRow> Quotas);

    private sealed record SummaryResponse(
        Guid PayeeId, string PayeeName, IReadOnlyList<CurrencyRow> ByCurrency);

    private sealed record CurrencyRow(
        string Currency, decimal EarnedCommissionsInPeriod, decimal AwaitingPaymentAllTime,
        decimal OutstandingDebt, decimal NetPendingPayout);

    private sealed record QuotaRow(
        Guid QuotaId, string PlanName, decimal TargetAmount, string Currency, string Measurement,
        decimal AchievedAmount, decimal AttainmentRatio, string AttainmentSource);

    // ══ 1. A rep sees their own money ════════════════════════════════════════

    [Fact]
    public async Task A_linked_rep_sees_their_own_figures()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        var payeeId = await SeedPayeeAsync($"MD1{Guid.NewGuid():N}"[..12], userId);
        await SeedPayoutAsync(payeeId, 10_000m);

        var body = await ReadAsync(await AskAsync(userId));

        body.Linked.Should().BeTrue();
        body.PayeeId.Should().Be(payeeId);
        body.Summary!.ByCurrency.Single(c => c.Currency == Eur)
            .AwaitingPaymentAllTime.Should().Be(10_000m);
    }

    // ══ 2. The false zero ════════════════════════════════════════════════════

    [Fact]
    public async Task An_account_with_no_payee_is_told_so_rather_than_shown_zeros()
    {
        var body = await ReadAsync(await AskAsync($"user-{Guid.NewGuid():N}"));

        // ★★ Not an empty summary with Linked true: that renders as "you earned nothing", which is a
        // different statement from "you are not in the payee list".
        body.Linked.Should().BeFalse();
        body.PayeeId.Should().BeNull();
        body.Summary.Should().BeNull();
        body.Quotas.Should().BeEmpty();
    }

    // ══ 3. One URL, two answers ══════════════════════════════════════════════

    [Fact]
    public async Task Two_reps_asking_the_same_url_get_their_own_payee()
    {
        var userA = $"user-{Guid.NewGuid():N}";
        var userB = $"user-{Guid.NewGuid():N}";
        var payeeA = await SeedPayeeAsync($"MDA{Guid.NewGuid():N}"[..12], userA);
        var payeeB = await SeedPayeeAsync($"MDB{Guid.NewGuid():N}"[..12], userB);

        await SeedPayoutAsync(payeeA, 7_000m);

        var a = await ReadAsync(await AskAsync(userA));
        var b = await ReadAsync(await AskAsync(userB));

        a.PayeeId.Should().Be(payeeA);
        b.PayeeId.Should().Be(payeeB);

        // B has no payouts: their figures must be theirs, not a leak of A's 7,000.
        b.Summary!.ByCurrency.Sum(c => c.AwaitingPaymentAllTime).Should().Be(0m);
    }

    // ══ 4. No quota in effect is an empty list, not a fabricated 0% ═══════════

    [Fact]
    public async Task A_rep_with_no_quota_running_gets_no_quota_rows()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        await SeedPayeeAsync($"MDQ{Guid.NewGuid():N}"[..12], userId);

        var body = await ReadAsync(await AskAsync(userId));

        body.Linked.Should().BeTrue();
        body.Quotas.Should().BeEmpty();
    }

    // ══ 5. Every role that can sign in has a screen ══════════════════════════

    [Fact]
    public async Task The_screen_answers_every_role_that_can_sign_in()
    {
        foreach (var role in new[] { "Rep", "Manager", "CompManager", "TenantAdmin" })
        {
            var userId = $"user-{Guid.NewGuid():N}";
            (await AskAsync(userId, role)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
