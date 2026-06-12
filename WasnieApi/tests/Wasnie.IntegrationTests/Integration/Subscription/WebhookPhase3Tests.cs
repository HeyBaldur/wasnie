using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Subscription;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class WebhookPhase3Tests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _anon = null!;

    private const string CustomerId = "cus_test_webhook_phase3";
    private const string SubscriptionId = "sub_test_webhook_phase3";

    public WebhookPhase3Tests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _anon = _fixture.Factory.CreateClient();

        // Seed an active Growth subscription for TenantA that we can mutate via webhooks.
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tid = TestConstants.TenantA;

        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM UserSubscriptions WHERE TenantId = {tid}");
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM ProcessedStripeEvents WHERE EventId LIKE 'evt_webhook_p3_%'");

        var now = DateTimeOffset.UtcNow;
        var sub = UserSubscription.CreateFree(Guid.NewGuid(), tid, "test@wasnie.io", now);
        sub.UpdateFromStripe(
            tier: Tier.Growth,
            status: SubscriptionStatus.Active,
            stripeSubscriptionId: SubscriptionId,
            stripeCustomerId: CustomerId,
            stripePriceId: "price_growth",
            stripeProductId: "prod_growth",
            periodStart: now,
            periodEnd: now.AddMonths(1),
            nextBillingDate: now.AddMonths(1),
            now: now);
        db.UserSubscriptions.Add(sub);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tid = TestConstants.TenantA;
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM UserSubscriptions WHERE TenantId = {tid}");
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM ProcessedStripeEvents WHERE EventId LIKE 'evt_webhook_p3_%'");
    }

    // ── invoice.payment_failed → PastDue ────────────────────────────────────────

    [Fact]
    public async Task PaymentFailed_SetsSubscriptionPastDue()
    {
        var json = BuildInvoiceEvent("invoice.payment_failed", "evt_webhook_p3_pf1");
        var response = await PostWebhook(json);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var sub = await ReadSubscription();
        sub!.Status.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public async Task PaymentFailed_IsIdempotent_ProcessedTwiceNoError()
    {
        var json = BuildInvoiceEvent("invoice.payment_failed", "evt_webhook_p3_pf2");

        var r1 = await PostWebhook(json);
        var r2 = await PostWebhook(json);

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── invoice.payment_succeeded → recover from PastDue ────────────────────────

    [Fact]
    public async Task PaymentSucceeded_WhenPastDue_RecoversMakesActive()
    {
        // First mark as PastDue
        await PostWebhook(BuildInvoiceEvent("invoice.payment_failed", "evt_webhook_p3_ps_fail"));

        var after_fail = await ReadSubscription();
        after_fail!.Status.Should().Be(SubscriptionStatus.PastDue);

        // Now recover
        var r = await PostWebhook(BuildInvoiceEvent("invoice.payment_succeeded", "evt_webhook_p3_ps_succeed"));
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var sub = await ReadSubscription();
        sub!.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task PaymentSucceeded_WhenAlreadyActive_IsNoOp()
    {
        // Subscription starts Active — payment_succeeded should not break it
        var r = await PostWebhook(BuildInvoiceEvent("invoice.payment_succeeded", "evt_webhook_p3_noop_succeed"));
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var sub = await ReadSubscription();
        sub!.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task PaymentSucceeded_IsIdempotent()
    {
        await PostWebhook(BuildInvoiceEvent("invoice.payment_failed", "evt_webhook_p3_idem_fail"));

        var json = BuildInvoiceEvent("invoice.payment_succeeded", "evt_webhook_p3_idem_succeed");
        var r1 = await PostWebhook(json);
        var r2 = await PostWebhook(json);

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);

        var sub = await ReadSubscription();
        sub!.Status.Should().Be(SubscriptionStatus.Active);
    }

    // ── customer.subscription.deleted → Canceled ────────────────────────────────

    [Fact]
    public async Task SubscriptionDeleted_SetsStatusCanceled()
    {
        var json = BuildSubscriptionDeletedEvent("evt_webhook_p3_del1");
        var response = await PostWebhook(json);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var sub = await ReadSubscription();
        sub!.Status.Should().Be(SubscriptionStatus.Canceled);
    }

    [Fact]
    public async Task SubscriptionDeleted_IsIdempotent()
    {
        var json = BuildSubscriptionDeletedEvent("evt_webhook_p3_del2");
        var r1 = await PostWebhook(json);
        var r2 = await PostWebhook(json);

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SubscriptionDeleted_WhenCancelScheduled_ClearsCancelScheduleFields()
    {
        // Arrange: mark the subscription as cancel-scheduled (simulates the scenario from the bug report)
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sub = await db.UserSubscriptions
                .IgnoreQueryFilters()
                .FirstAsync(s => s.TenantId == TestConstants.TenantA);
            sub.ScheduleCancellation(DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        // Act: fire subscription.deleted
        var json = BuildSubscriptionDeletedEvent("evt_webhook_p3_del_clear");
        var response = await PostWebhook(json);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: schedule cleared, status=Canceled, CanceledAt populated
        var result = await ReadSubscription();
        result!.Status.Should().Be(SubscriptionStatus.Canceled);
        result.CancelAtPeriodEnd.Should().BeFalse();
        result.CancelAt.Should().BeNull();
        result.CanceledAt.Should().NotBeNull();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> PostWebhook(string json)
    {
        var sig = BuildStripeSignature(json, TestConstants.StripeWebhookSecret);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/subscription/webhook")
        {
            Content = content,
        };
        request.Headers.Add("Stripe-Signature", sig);
        return await _anon.SendAsync(request);
    }

    private static string BuildStripeSignature(string payload, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signed = $"{timestamp}.{payload}";
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var signedBytes = Encoding.UTF8.GetBytes(signed);

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(signedBytes);
        var hexHash = Convert.ToHexString(hash).ToLowerInvariant();

        return $"t={timestamp},v1={hexHash}";
    }

    private async Task<UserSubscription?> ReadSubscription()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.UserSubscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == TestConstants.TenantA);
    }

    private static string BuildInvoiceEvent(string eventType, string eventId)
    {
        var obj = new
        {
            id = $"in_{eventId}",
            @object = "invoice",
            customer = CustomerId,
            subscription = SubscriptionId,
            status = eventType == "invoice.payment_failed" ? "open" : "paid",
        };

        return JsonSerializer.Serialize(new
        {
            id = eventId,
            @object = "event",
            type = eventType,
            api_version = "2024-04-10",
            data = new { @object = obj },
        });
    }

    private static string BuildSubscriptionDeletedEvent(string eventId)
    {
        var obj = new
        {
            id = SubscriptionId,
            @object = "subscription",
            customer = CustomerId,
            status = "canceled",
        };

        return JsonSerializer.Serialize(new
        {
            id = eventId,
            @object = "event",
            type = "customer.subscription.deleted",
            api_version = "2024-04-10",
            data = new { @object = obj },
        });
    }
}
