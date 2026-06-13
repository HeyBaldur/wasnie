using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Subscription;

/// <summary>
/// End-to-end coverage of the failed-payment lifecycle via synthetic Stripe webhook events.
///
/// Approach: Approach A — synthetic webhook payloads processed through the full HTTP stack
/// (StripeWebhookService + SubscriptionEnforcementMiddleware). No real Stripe dependency.
///
/// Covers five cases required by WI-TEST-PAYMENT-CYCLE:
///   Case 1 — payment_failed → PastDue: tier intact, audit SUBSCRIPTION_PAST_DUE logged,
///             enforcement does NOT block (PastDue tenants retain access).
///   Case 2 — payment_succeeded (from PastDue) → Active: audit SUBSCRIPTION_RECOVERED logged.
///   Case 3 — subscription.deleted (from PastDue) → Canceled: CanceledAt set, cancel-schedule
///             fields cleared, audit SUBSCRIPTION_CANCELED logged, enforcement returns 402.
///   Case 4 — idempotency: same Stripe event ID processed twice → ProcessedStripeEvents count = 1.
///   Case 5 — multi-tenant isolation: payment_failed for TenantA's customerId leaves TenantB intact.
///
/// Manual Stripe test-clock verification (for future use):
///   stripe customers create --metadata "wasnie_tenant_id=..." to link a real tenant.
///   stripe test_helpers test_clocks create --frozen-time (unix epoch now).
///   Attach card 4000000000000341 (always_fails next payment) to the customer.
///   stripe subscriptions create --customer cus_... --items ... --test-clock clk_...
///   stripe test_helpers test_clocks advance --id clk_... --frozen-time (epoch + 32 days).
///   The generated invoice.payment_failed will reach the webhook and trigger PastDue.
///   Repeat advance past retry window to trigger subscription.deleted → Canceled.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PaymentFailedCycleTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _anon = null!;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    // Unique IDs scoped to this test class to avoid cross-class contamination.
    private const string CustomerIdA = "cus_cyc_test_a";
    private const string CustomerIdB = "cus_cyc_test_b";
    private const string SubscriptionIdA = "sub_cyc_test_a";
    private const string SubscriptionIdB = "sub_cyc_test_b";

    // All Stripe event IDs in this class share this prefix for cleanup.
    private const string EventIdPrefix = "evt_cyc_";

    public PaymentFailedCycleTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _anon   = _fixture.Factory.CreateClient();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        // Clean any leftovers from a prior run of this class.
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM UserSubscriptions WHERE TenantId IN ({TestConstants.TenantA}, {TestConstants.TenantB})");
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM ProcessedStripeEvents WHERE EventId LIKE '{EventIdPrefix}%'");

        // Seed TenantA — Active Growth subscription.
        var subA = UserSubscription.CreateFree(Guid.NewGuid(), TestConstants.TenantA, "a@wasnie.io", now);
        subA.UpdateFromStripe(
            tier: Tier.Growth,
            status: SubscriptionStatus.Active,
            stripeSubscriptionId: SubscriptionIdA,
            stripeCustomerId: CustomerIdA,
            stripePriceId: "price_growth",
            stripeProductId: "prod_growth",
            periodStart: now,
            periodEnd: now.AddMonths(1),
            nextBillingDate: now.AddMonths(1),
            now: now);
        db.UserSubscriptions.Add(subA);

        // Seed TenantB — Active Growth subscription with a different customer ID.
        var subB = UserSubscription.CreateFree(Guid.NewGuid(), TestConstants.TenantB, "b@wasnie.io", now);
        subB.UpdateFromStripe(
            tier: Tier.Growth,
            status: SubscriptionStatus.Active,
            stripeSubscriptionId: SubscriptionIdB,
            stripeCustomerId: CustomerIdB,
            stripePriceId: "price_growth",
            stripeProductId: "prod_growth",
            periodStart: now,
            periodEnd: now.AddMonths(1),
            nextBillingDate: now.AddMonths(1),
            now: now);
        db.UserSubscriptions.Add(subB);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM UserSubscriptions WHERE TenantId IN ({TestConstants.TenantA}, {TestConstants.TenantB})");
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM ProcessedStripeEvents WHERE EventId LIKE '{EventIdPrefix}%'");
        // AuditLogs are immutable (DB trigger prevents DELETE) — queried by ResourceId, never cleaned.
    }

    // ── Case 1: payment_failed → PastDue ────────────────────────────────────────

    [Fact]
    public async Task PaymentFailed_StatusIsPastDue_TierUnchanged_AuditPastDueLogged()
    {
        const string invoiceId = "in_cyc_pf_tier_audit";
        var r = await PostWebhook(BuildInvoiceEvent("invoice.payment_failed", $"{EventIdPrefix}pf_tier_audit", CustomerIdA, SubscriptionIdA, invoiceId));
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var sub = await ReadSubscription(TestConstants.TenantA);
        sub!.Status.Should().Be(SubscriptionStatus.PastDue, "payment_failed marks subscription PastDue");
        sub.Tier.Should().Be(Tier.Growth, "payment failure must not alter the tier");

        var auditEntry = await ReadAuditByResourceId(invoiceId, AuditActions.SubscriptionPastDue);
        auditEntry.Should().NotBeNull("SUBSCRIPTION_PAST_DUE must be written to the audit log");
        auditEntry!.TenantId.Should().Be(TestConstants.TenantA);
        auditEntry.ActorUserId.Should().Be("stripe-webhook");
    }

    // ── Case 1 (enforcement): PastDue does NOT block access ─────────────────────

    [Fact]
    public async Task PastDue_EnforcementDoesNotBlock_FunctionalEndpointReturnsNot402()
    {
        // Drive TenantA to PastDue via webhook.
        await PostWebhook(BuildInvoiceEvent(
            "invoice.payment_failed", $"{EventIdPrefix}pf_access",
            CustomerIdA, SubscriptionIdA, "in_cyc_pf_access"));

        var sub = await ReadSubscription(TestConstants.TenantA);
        sub!.Status.Should().Be(SubscriptionStatus.PastDue);

        // Enforcement only blocks Canceled — PastDue tenants retain access.
        var response = await _clientA.GetAsync("/api/payees");
        response.StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired,
            "PastDue tenants must not be blocked by SubscriptionEnforcementMiddleware");
    }

    // ── Case 2: PastDue → payment_succeeded → Active ────────────────────────────

    [Fact]
    public async Task PaymentSucceeded_FromPastDue_StatusActive_TierGrowth_AuditRecoveredLogged()
    {
        const string failInvoiceId   = "in_cyc_rec_fail";
        const string succeedInvoiceId = "in_cyc_rec_succ";

        await PostWebhook(BuildInvoiceEvent(
            "invoice.payment_failed", $"{EventIdPrefix}rec_fail",
            CustomerIdA, SubscriptionIdA, failInvoiceId));

        var afterFail = await ReadSubscription(TestConstants.TenantA);
        afterFail!.Status.Should().Be(SubscriptionStatus.PastDue);

        var r = await PostWebhook(BuildInvoiceEvent(
            "invoice.payment_succeeded", $"{EventIdPrefix}rec_succ",
            CustomerIdA, SubscriptionIdA, succeedInvoiceId));
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var sub = await ReadSubscription(TestConstants.TenantA);
        sub!.Status.Should().Be(SubscriptionStatus.Active, "payment_succeeded must recover a PastDue subscription");
        sub.Tier.Should().Be(Tier.Growth, "tier must be preserved across recovery");

        var auditEntry = await ReadAuditByResourceId(succeedInvoiceId, AuditActions.SubscriptionRecovered);
        auditEntry.Should().NotBeNull("SUBSCRIPTION_RECOVERED must be written to the audit log");
        auditEntry!.TenantId.Should().Be(TestConstants.TenantA);
    }

    // ── Case 3: PastDue → subscription.deleted → Canceled + enforcement 402 ─────

    [Fact]
    public async Task SubscriptionDeleted_FromPastDue_StatusCanceled_FieldsClean_AuditCanceledLogged_EnforcementBlocks()
    {
        // First drive to PastDue.
        await PostWebhook(BuildInvoiceEvent(
            "invoice.payment_failed", $"{EventIdPrefix}del_fail",
            CustomerIdA, SubscriptionIdA, "in_cyc_del_fail"));

        var afterFail = await ReadSubscription(TestConstants.TenantA);
        afterFail!.Status.Should().Be(SubscriptionStatus.PastDue);

        // Now cancel via subscription.deleted.
        var r = await PostWebhook(BuildSubscriptionDeletedEvent($"{EventIdPrefix}del_cancel", CustomerIdA, SubscriptionIdA));
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var sub = await ReadSubscription(TestConstants.TenantA);
        sub!.Status.Should().Be(SubscriptionStatus.Canceled, "subscription.deleted must set Status=Canceled");
        sub.CanceledAt.Should().NotBeNull("CanceledAt must be populated on deletion");
        sub.CancelAtPeriodEnd.Should().BeFalse("CancelAtPeriodEnd must be cleared on hard deletion");
        sub.CancelAt.Should().BeNull("CancelAt must be cleared on hard deletion");

        var auditEntry = await ReadAuditByResourceId(SubscriptionIdA, AuditActions.SubscriptionCanceled);
        auditEntry.Should().NotBeNull("SUBSCRIPTION_CANCELED must be written to the audit log");
        auditEntry!.TenantId.Should().Be(TestConstants.TenantA);

        // Enforcement must now block functional endpoints.
        var blocked = await _clientA.GetAsync("/api/payees");
        blocked.StatusCode.Should().Be(HttpStatusCode.PaymentRequired,
            "Canceled tenants must be blocked by SubscriptionEnforcementMiddleware");
    }

    // ── Case 4: idempotency ──────────────────────────────────────────────────────

    [Fact]
    public async Task Idempotency_SameEventId_ProcessedStripeEventInsertedOnce_StatusUnchanged()
    {
        const string eventId = $"{EventIdPrefix}idem_pf";
        var json = BuildInvoiceEvent("invoice.payment_failed", eventId, CustomerIdA, SubscriptionIdA, "in_cyc_idem_pf");

        var r1 = await PostWebhook(json);
        var r2 = await PostWebhook(json);

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK, "second delivery of the same event must not fail");

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dedupCount = await db.ProcessedStripeEvents
            .CountAsync(e => e.EventId == eventId);
        dedupCount.Should().Be(1, "ProcessedStripeEvents must record the event exactly once (idempotency key)");

        var sub = await ReadSubscription(TestConstants.TenantA);
        sub!.Status.Should().Be(SubscriptionStatus.PastDue, "status is the result of one processing, not two");
    }

    // ── Case 5: multi-tenant isolation ──────────────────────────────────────────

    [Fact]
    public async Task MultiTenant_PaymentFailed_ForTenantA_DoesNotAffectTenantB()
    {
        // Fire payment_failed scoped to TenantA's customer ID.
        var r = await PostWebhook(BuildInvoiceEvent(
            "invoice.payment_failed", $"{EventIdPrefix}mt_pf",
            CustomerIdA, SubscriptionIdA, "in_cyc_mt_pf"));
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var subA = await ReadSubscription(TestConstants.TenantA);
        subA!.Status.Should().Be(SubscriptionStatus.PastDue, "TenantA must be marked PastDue");

        var subB = await ReadSubscription(TestConstants.TenantB);
        subB!.Status.Should().Be(SubscriptionStatus.Active,
            "TenantB must be unaffected — webhook lookup is by StripeCustomerId, not tenantId");
        subB.Tier.Should().Be(Tier.Growth, "TenantB tier must be unchanged");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

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
        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signed));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private async Task<UserSubscription?> ReadSubscription(Guid tenantId)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.UserSubscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId);
    }

    private async Task<AuditLog?> ReadAuditByResourceId(string resourceId, string action)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ResourceId == resourceId && a.Action == action);
    }

    // ── Event builders ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a synthetic invoice.payment_failed or invoice.payment_succeeded event.
    /// Payload reflects billing_mode=flexible (dahlia) — customer and subscription IDs
    /// are the primary lookup keys; item-level period fields are not required for these
    /// invoice handlers (they only call MarkPastDue / Recover on the found subscription).
    /// </summary>
    private static string BuildInvoiceEvent(
        string eventType,
        string eventId,
        string customerId,
        string subscriptionId,
        string invoiceId)
    {
        var invoiceObj = new
        {
            id = invoiceId,
            @object = "invoice",
            customer = customerId,
            subscription = subscriptionId,
            status = eventType == "invoice.payment_failed" ? "open" : "paid",
            billing_reason = "subscription_cycle",
        };

        return JsonSerializer.Serialize(new
        {
            id = eventId,
            @object = "event",
            type = eventType,
            api_version = "2024-04-10",
            data = new { @object = invoiceObj },
        });
    }

    /// <summary>
    /// Builds a synthetic customer.subscription.deleted event.
    /// The handler looks up UserSubscription by StripeCustomerId (not SubscriptionId),
    /// so both are included for payload completeness.
    /// </summary>
    private static string BuildSubscriptionDeletedEvent(
        string eventId,
        string customerId,
        string subscriptionId)
    {
        var subObj = new
        {
            id = subscriptionId,
            @object = "subscription",
            customer = customerId,
            status = "canceled",
        };

        return JsonSerializer.Serialize(new
        {
            id = eventId,
            @object = "event",
            type = "customer.subscription.deleted",
            api_version = "2024-04-10",
            data = new { @object = subObj },
        });
    }
}
