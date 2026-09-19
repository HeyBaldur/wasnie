using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Ledger;
using Wasnie.Application.Compensation.Queries.Ledger;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Enums;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-98: the ledger summary answers an EXPLICIT window, not only a preset token.
///
/// ★★ WHY THE TOKENS WERE NOT ENOUGH. "this-month", "last-month", "ytd" cannot express "1 February to
/// 15 April", and a person looking at their own pay asks about the dates they remember, not about a
/// preset somebody chose in advance. The personal dashboard needed to be driven by the same range
/// picker the company dashboard has had since KAN-62, and this query is where its money comes from.
///
/// ★★ THE DANGEROUS HALF IS NOT THE FILTERING, IT IS THE LABEL AND THE FIGURES THAT MUST NOT FILTER.
/// A summary stamped "all-time" over a window of March is one field meaning two things, on a screen
/// about somebody's pay, with no second source to catch it. And debt has no period dimension at all —
/// the ledger records a running total, so "what did they owe in March" is not a question the data can
/// answer, and a window that appeared to answer it would be inventing a figure.
/// </summary>
public sealed class PayeeLedgerSummaryWindowTests
{
    private const string Eur = "EUR";

    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(ApplicationDbContext Db, GetPayeeLedgerSummaryHandler Handler, Guid TenantId, Guid PayeeId);

    private static Harness Seed(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<IPublisher>());

        var payee = Payee.Create(tenantId, "Ana Garcia", "EMP-MINE", "ana@acme.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        db.Payees.Add(payee);
        db.SaveChanges();

        var auth = Substitute.For<IAuthorizationService>();
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var guard = Substitute.For<IPayeeAccessGuard>();
        guard.CanReadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.UtcDateTime);
        clock.UtcNowOffset.Returns(Now);

        return new Harness(
            db, new GetPayeeLedgerSummaryHandler(db, auth, guard, clock), tenantId, payee.Id);
    }

    /// <summary>
    /// A payout of <paramref name="amount"/> covering <paramref name="period"/>. When
    /// <paramref name="paidAt"/> is given the payout is marked Paid on that instant, which is the date
    /// the CASH figure is attributed by — deliberately a different predicate from the accrual one.
    /// </summary>
    private static void SeedPayout(
        Harness h, decimal amount, DateRange period, DateTimeOffset? paidAt = null)
    {
        var spec = new PayoutLineSpec(
            Guid.NewGuid(), Guid.NewGuid(), "Base",
            Money.Of(amount * 10m, Eur), Money.Of(amount, Eur), []);

        var payout = CompensationPayout.Calculate(
            h.TenantId, h.PayeeId, Guid.NewGuid(),
            PayeeReference.Snapshot(h.PayeeId, "Ana Garcia", "EMP-MINE"),
            period, [spec], Eur, "test", Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);

        if (paidAt.HasValue)
        {
            payout.Approve("test", Now, Guid.NewGuid());
            payout.MarkPaid("test", paidAt.Value);
        }

        h.Db.CompensationPayouts.Add(payout);
        h.Db.SaveChanges();
    }

    private static void SeedDebt(Harness h, decimal debt)
    {
        var entry = PayeeLedgerEntry.CreateSystemEntry(
            h.TenantId, h.PayeeId, LedgerTransactionType.ClawbackDebit, Money.Of(debt, Eur),
            "Deal churned.", LedgerSourceType.DealChurn, "system", Guid.NewGuid(), Now, Guid.NewGuid());

        var balance = PayeeBalance.Open(h.TenantId, h.PayeeId, Eur, Guid.NewGuid(), Now);
        balance.Apply(entry, Now);

        h.Db.PayeeLedgerEntries.Add(entry);
        h.Db.PayeeBalances.Add(balance);
        h.Db.SaveChanges();
    }

    private static async Task<PayeeCurrencyBalanceDto> RunAsync(
        Harness h, DateOnly? from, DateOnly? to, string period = "all-time")
    {
        var result = await h.Handler.Handle(
            new GetPayeeLedgerSummaryQuery(h.PayeeId, period, from, to), default);

        result.IsSuccess.Should().BeTrue();
        return result.Value!.ByCurrency.Single(c => c.Currency == Eur);
    }

    // ── The window filters what it can ────────────────────────────────────

    /// <summary>
    /// ★★ ACCRUAL IS FILTERED BY PERIOD INTERSECTION — the same predicate the payouts screen uses, so
    /// the screen and this query cannot disagree about what "July" contains.
    /// </summary>
    [Fact]
    public async Task Earned_counts_only_payouts_whose_period_meets_the_window()
    {
        var h = Seed(nameof(Earned_counts_only_payouts_whose_period_meets_the_window));
        SeedPayout(h, 100m, DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)));
        SeedPayout(h, 500m, DateRange.Of(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));

        var july = await RunAsync(h, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        july.EarnedCommissionsInPeriod.Should().Be(100m);
    }

    /// <summary>
    /// ★★ CASH IS FILTERED BY WHEN IT LEFT, NOT BY WHAT IT WAS FOR. A payout covering July and paid in
    /// September is September's cash: attributing it by the compensation period reports July's money in
    /// December, which is the bug that taught this codebase to keep the two predicates apart.
    /// </summary>
    [Fact]
    public async Task Paid_counts_by_when_the_money_left_not_by_what_it_was_for()
    {
        var h = Seed(nameof(Paid_counts_by_when_the_money_left_not_by_what_it_was_for));
        SeedPayout(
            h, 100m,
            DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            paidAt: new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));

        var july = await RunAsync(h, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var september = await RunAsync(h, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        // Accrued in July, because that is the period it covers…
        july.EarnedCommissionsInPeriod.Should().Be(100m);
        july.PaidOutInPeriod.Should().Be(0m);

        // …but the cash moved in September.
        september.PaidOutInPeriod.Should().Be(100m);
    }

    /// <summary>
    /// ★ THE WHOLE FINAL DAY IS INSIDE THE WINDOW. PaidAt is an instant and the bound is a date; a
    /// payment made at 18:00 on the last day of the window must not fall outside it.
    /// </summary>
    [Fact]
    public async Task A_payment_late_on_the_final_day_is_inside_the_window()
    {
        var h = Seed(nameof(A_payment_late_on_the_final_day_is_inside_the_window));
        SeedPayout(
            h, 100m,
            DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            paidAt: new DateTimeOffset(2026, 7, 31, 18, 30, 0, TimeSpan.Zero));

        var july = await RunAsync(h, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        july.PaidOutInPeriod.Should().Be(100m);
    }

    /// <summary>★ Half-open is allowed and means "from here on".</summary>
    [Fact]
    public async Task A_lower_bound_alone_means_from_there_onwards()
    {
        var h = Seed(nameof(A_lower_bound_alone_means_from_there_onwards));
        SeedPayout(h, 100m, DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
        SeedPayout(h, 500m, DateRange.Of(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));

        var row = await RunAsync(h, new DateOnly(2026, 8, 1), null);

        row.EarnedCommissionsInPeriod.Should().Be(500m);
    }

    // ── What the window must NOT touch ────────────────────────────────────

    /// <summary>
    /// ★★ DEBT IS AS OF NOW, WHATEVER WINDOW ARRIVES. The ledger has no period dimension: an entry
    /// carries the date it was booked, and a balance is a running total with no concept of a cycle. A
    /// window that appeared to scope it would produce a figure that LOOKS period-scoped and is not — and
    /// on a pay screen that is money quietly going missing.
    /// </summary>
    [Fact]
    public async Task Debt_is_not_scoped_by_the_window()
    {
        var h = Seed(nameof(Debt_is_not_scoped_by_the_window));
        SeedDebt(h, 250m);

        var longAgo = await RunAsync(h, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31));

        longAgo.OutstandingDebt.Should().Be(250m);
    }

    /// <summary>
    /// ★★ AND SO IS AWAITING PAYMENT. Money earned last quarter and still unpaid is still owed today;
    /// filtering it by the window would hide exactly the case somebody opens this screen to check.
    /// </summary>
    [Fact]
    public async Task Awaiting_payment_is_not_scoped_by_the_window()
    {
        var h = Seed(nameof(Awaiting_payment_is_not_scoped_by_the_window));
        SeedPayout(h, 400m, DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));

        var september = await RunAsync(h, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        september.EarnedCommissionsInPeriod.Should().Be(0m);
        september.AwaitingPaymentAllTime.Should().Be(400m);
    }

    // ── The label says which of the two was used ──────────────────────────

    /// <summary>
    /// ★★ THE ANSWER MUST NOT CARRY A TOKEN IT DID NOT USE. Passing both an explicit window and the
    /// default "all-time" is the ordinary call the personal dashboard makes; a summary that came back
    /// stamped "all-time" over one month would be read as all-time by anything that trusts the label.
    /// </summary>
    [Fact]
    public async Task An_explicit_window_labels_itself_custom_and_ignores_the_token()
    {
        var h = Seed(nameof(An_explicit_window_labels_itself_custom_and_ignores_the_token));
        SeedPayout(h, 100m, DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)));

        var result = await h.Handler.Handle(
            new GetPayeeLedgerSummaryQuery(
                h.PayeeId, "all-time", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            default);

        result.Value!.PeriodLabel.Should().Be("custom");
        result.Value.PeriodStart.Should().Be(new DateOnly(2026, 7, 1));
        result.Value.PeriodEnd.Should().Be(new DateOnly(2026, 7, 31));
    }

    /// <summary>
    /// ★ AND WITHOUT A WINDOW NOTHING CHANGES. Every existing caller passes a token and no dates; this
    /// pins that they still get exactly what they got before.
    /// </summary>
    [Fact]
    public async Task Without_a_window_the_token_still_decides()
    {
        var h = Seed(nameof(Without_a_window_the_token_still_decides));
        SeedPayout(h, 100m, DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));

        var result = await h.Handler.Handle(
            new GetPayeeLedgerSummaryQuery(h.PayeeId), default);

        result.Value!.PeriodLabel.Should().Be("all-time");
        result.Value.PeriodStart.Should().BeNull();
        result.Value.PeriodEnd.Should().BeNull();
        result.Value.ByCurrency.Single().EarnedCommissionsInPeriod.Should().Be(100m);
    }
}
