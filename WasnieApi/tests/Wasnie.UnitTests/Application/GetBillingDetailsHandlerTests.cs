using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Stripe;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription;
using Wasnie.Application.Features.Subscription.Handlers;
using Wasnie.Application.Features.Subscription.Queries;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Persistence;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// "Manage billing" reads the card and invoices live from Stripe. What matters: the permission gate runs before any
/// Stripe call, a trial (no Stripe customer) is an empty result and not an error, amounts leave Stripe's minor units
/// correctly per currency, and a Stripe outage is a failure the page can show — never an exception.
/// </summary>
public sealed class GetBillingDetailsHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _auth = Substitute.For<IAuthorizationService>();
    private readonly IStripeBillingDetailsReader _stripe = Substitute.For<IStripeBillingDetailsReader>();
    private readonly IStripeSubscriptionReconciler _reconciler = Substitute.For<IStripeSubscriptionReconciler>();

    public GetBillingDetailsHandlerTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        tenantCtx.IsResolved.Returns(true);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());
    }

    [Fact]
    public async Task Handle_WithoutPermission_ThrowsBeforeCallingStripe()
    {
        await SeedSubscriptionAsync("cus_test", "sub_test");
        _auth.RequireAsync(Permission.SubscriptionManage, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ForbiddenException(Permission.SubscriptionManage));

        var act = () => Create().Handle(new GetBillingDetailsQuery(), default);

        await act.Should().ThrowAsync<ForbiddenException>();
        await _stripe.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default, default);
    }

    [Fact]
    public async Task Handle_NoSubscription_ReturnsEmptyWithoutCallingStripe()
    {
        var result = await Create().Handle(new GetBillingDetailsQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaymentMethod.Should().BeNull();
        result.Value.Invoices.Should().BeEmpty();
        await _stripe.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default, default);
    }

    [Fact]
    public async Task Handle_SubscriptionWithoutStripeCustomer_ReturnsEmpty()
    {
        _db.UserSubscriptions.Add(UserSubscription.CreatePending(Guid.NewGuid(), TenantId, "t@t.io", Now));
        await _db.SaveChangesAsync();

        var result = await Create().Handle(new GetBillingDetailsQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Invoices.Should().BeEmpty();
        await _stripe.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default, default);
    }

    [Fact]
    public async Task Handle_MapsPaymentMethodAndInvoices_ConvertingMinorUnits()
    {
        await SeedSubscriptionAsync("cus_test", "sub_test");
        var created = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        _stripe.GetAsync("cus_test", "sub_test", GetBillingDetailsHandler.InvoiceLimit, Arg.Any<CancellationToken>())
            .Returns(new StripeBillingDetails(
                new StripeSubscriptionSnapshot("active", created, created.AddMonths(1), false, null, null),
                new StripePaymentMethodSummary("card", "visa", "4242", 12, 2028),
                [
                    // A settled invoice carries no retry; the open one has been attempted and is scheduled again.
                    new StripeInvoiceSummary("in_1", "A1-0001", "Pro", created, 29900, "eur", "paid", "https://h", "https://p", null, 1),
                    new StripeInvoiceSummary("in_2", "A1-0002", null, created, 1000, "jpy", "open", null, null, created.AddDays(3), 4),
                ]));

        var result = await Create().Handle(new GetBillingDetailsQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Subscription!.Status.Should().Be("active");
        result.Value.Subscription.CurrentPeriodEnd.Should().Be(new DateTimeOffset(created.AddMonths(1)));
        result.Value.PaymentMethod!.Last4.Should().Be("4242");
        result.Value.PaymentMethod.Brand.Should().Be("visa");

        var eur = result.Value.Invoices[0];
        eur.Total.Should().Be(299.00m);
        eur.Currency.Should().Be("EUR");
        eur.Status.Should().Be("paid");
        eur.CreatedAt.Should().Be(new DateTimeOffset(created));
        eur.InvoicePdfUrl.Should().Be("https://p");

        result.Value.Invoices[1].Total.Should().Be(1000m, "JPY is a zero-decimal currency");
    }

    [Fact]
    public async Task Handle_StripeFails_ReturnsFailure()
    {
        await SeedSubscriptionAsync("cus_test", "sub_test");
        _stripe.GetAsync(default!, default, default, default).ReturnsForAnyArgs<StripeBillingDetails>(
            _ => throw new StripeException("boom"));

        var result = await Create().Handle(new GetBillingDetailsQuery(), default);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_StripeMatchesStoredRow_DoesNotSync()
    {
        await SeedSubscriptionAsync("cus_test", "sub_test");
        StripeReturns(new StripeSubscriptionSnapshot("active", Now.UtcDateTime, Now.AddMonths(1).UtcDateTime, false, null, null));

        var result = await Create().Handle(new GetBillingDetailsQuery(), default);

        result.Value!.Synced.Should().BeFalse();
        await _reconciler.DidNotReceiveWithAnyArgs().ReconcileAsync(default);
    }

    [Fact]
    public async Task Handle_StripeCanceledButRowActive_SyncsAndTellsTheClient()
    {
        await SeedSubscriptionAsync("cus_test", "sub_test");
        StripeReturns(new StripeSubscriptionSnapshot("canceled", Now.UtcDateTime, Now.AddMonths(1).UtcDateTime, false, null, Now.UtcDateTime));
        _reconciler.ReconcileAsync(Arg.Any<CancellationToken>()).Returns(true);

        var result = await Create().Handle(new GetBillingDetailsQuery(), default);

        result.Value!.Synced.Should().BeTrue();
        await _reconciler.Received(1).ReconcileAsync(Arg.Any<CancellationToken>());
    }

    // ── Drift detection ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Drift_MissedCancellation_Differs()
    {
        var row = Row();
        StripeSubscriptionDrift.Differs(row, Live("canceled")).Should().BeTrue();
        StripeSubscriptionDrift.Differs(row, Live("incomplete_expired")).Should().BeTrue();
    }

    [Fact]
    public void Drift_AlreadyCanceledRow_DoesNotDifferFromEndedStripe()
    {
        var row = Row();
        row.Cancel(Now);
        StripeSubscriptionDrift.Differs(row, Live("canceled")).Should().BeFalse();
    }

    [Fact]
    public void Drift_MissedRenewal_Differs()
    {
        var row = Row();
        StripeSubscriptionDrift.Differs(row, Live("active", periodEnd: Now.AddMonths(2).UtcDateTime)).Should().BeTrue();
    }

    [Fact]
    public void Drift_SubSecondNoise_DoesNotDiffer()
    {
        var row = Row();
        StripeSubscriptionDrift.Differs(row, Live("active", periodEnd: Now.AddMonths(1).UtcDateTime.AddMilliseconds(400))).Should().BeFalse();
    }

    [Fact]
    public void Drift_MissedStatusOrCancelSchedule_Differs()
    {
        var row = Row();
        StripeSubscriptionDrift.Differs(row, Live("past_due")).Should().BeTrue();
        StripeSubscriptionDrift.Differs(row, Live("active", cancelAtPeriodEnd: true)).Should().BeTrue();
    }

    [Theory]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("canceled", SubscriptionStatus.Canceled)]
    [InlineData("incomplete_expired", SubscriptionStatus.Incomplete)]
    [InlineData("trialing", SubscriptionStatus.Trialing)]
    public void StatusMap_IsTheWebhookMapping(string stripe, SubscriptionStatus expected) =>
        StripeSubscriptionStatus.Map(stripe).Should().Be(expected);

    [Fact]
    public void Cancel_WithKnownEndDate_RecordsWhenItEnded_NotWhenItWasNoticed()
    {
        var row = Row();
        var ended = Now.AddDays(-1);
        row.Cancel(Now, ended);
        row.CanceledAt.Should().Be(ended);
        row.UpdatedAt.Should().Be(Now);
    }

    private void StripeReturns(StripeSubscriptionSnapshot snapshot) =>
        _stripe.GetAsync(default!, default, default, default).ReturnsForAnyArgs(
            new StripeBillingDetails(snapshot, null, []));

    private static UserSubscription Row()
    {
        var sub = UserSubscription.CreatePending(Guid.NewGuid(), TenantId, "t@t.io", Now);
        sub.UpdateFromStripe("pro", SubscriptionStatus.Active, "sub_test", "cus_test",
            "price_pro", "prod_pro", Now, Now.AddMonths(1), Now.AddMonths(1), Now);
        return sub;
    }

    private static StripeSubscriptionSnapshot Live(string status, DateTime? periodEnd = null, bool cancelAtPeriodEnd = false) =>
        new(status, Now.UtcDateTime, periodEnd ?? Now.AddMonths(1).UtcDateTime, cancelAtPeriodEnd, null, null);

    [Theory]
    [InlineData(29900, "eur", "299.00")]
    [InlineData(1, "usd", "0.01")]
    [InlineData(0, "eur", "0")]
    [InlineData(-500, "eur", "-5.00")]
    [InlineData(1000, "JPY", "1000")]
    [InlineData(1000, "krw", "1000")]
    [InlineData(1230, "kwd", "1.230")]
    public void StripeAmount_ConvertsByCurrencyExponent(long minor, string currency, string expected)
    {
        StripeAmount.FromMinorUnits(minor, currency).Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    private GetBillingDetailsHandler Create() =>
        new(_db, TenantCtx(), _auth, _stripe, _reconciler, Substitute.For<ILogger<GetBillingDetailsHandler>>());

    private static ITenantContext TenantCtx()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(TenantId);
        ctx.IsResolved.Returns(true);
        return ctx;
    }

    private async Task SeedSubscriptionAsync(string customerId, string subscriptionId)
    {
        var sub = UserSubscription.CreatePending(Guid.NewGuid(), TenantId, "t@t.io", Now);
        sub.UpdateFromStripe("pro", SubscriptionStatus.Active, subscriptionId, customerId,
            "price_pro", "prod_pro", Now, Now.AddMonths(1), Now.AddMonths(1), Now);
        _db.UserSubscriptions.Add(sub);
        await _db.SaveChangesAsync();
    }

    public void Dispose() => _db.Dispose();
}
