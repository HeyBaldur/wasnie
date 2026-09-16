using Wasnie.UnitTests.TestDoubles;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Entities;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services;

namespace Wasnie.UnitTests.Services;

public sealed class StripeWebhookServiceTests : IDisposable
{
    private const string TestSecret = "whsec_test_1234567890abcdef12345678";
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ILogger<StripeWebhookService> _logger;
    private readonly IOptions<StripeOptions> _options;

    public StripeWebhookServiceTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(Guid.Empty);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        _audit = Substitute.For<IAuditService>();
        _clock = Substitute.For<IClock>();
        _logger = Substitute.For<ILogger<StripeWebhookService>>();

        _clock.UtcNowOffset.Returns(FixedNow);
        _clock.UtcNow.Returns(FixedNow.UtcDateTime);

        _options = Options.Create(new StripeOptions
        {
            SecretKey = "sk_test_fake",
            PublishableKey = "pk_test_fake",
            WebhookSecret = TestSecret,
            FrontendBaseUrl = "http://localhost:4200",
        });
    }

    // ── Signature verification ─────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_InvalidSignature_ReturnsFailure()
    {
        var result = await Create().ProcessAsync("{}", "t=0,v1=badhash");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ProcessAsync_BlankSignature_ReturnsFailure()
    {
        var result = await Create().ProcessAsync("{}", string.Empty);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAsync_InvalidSignature_DoesNotCallAudit()
    {
        await Create().ProcessAsync("{}", "bad_sig");

        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    // ── Idempotency (M2) ───────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_DuplicateEventId_ReturnsSuccess()
    {
        var eventId = "evt_already_processed";
        _db.ProcessedStripeEvents.Add(ProcessedStripeEvent.Create(eventId, FixedNow));
        await _db.SaveChangesAsync();

        var (json, sig) = Sign(BuildUnknownEventJson(eventId));
        var result = await Create().ProcessAsync(json, sig);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsync_DuplicateEventId_DoesNotCallAudit()
    {
        var eventId = "evt_dup_no_audit";
        _db.ProcessedStripeEvents.Add(ProcessedStripeEvent.Create(eventId, FixedNow));
        await _db.SaveChangesAsync();

        var (json, sig) = Sign(BuildUnknownEventJson(eventId));
        await Create().ProcessAsync(json, sig);

        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_DuplicateEventId_DoesNotAddAnotherProcessedEntry()
    {
        var eventId = "evt_dup_count";
        _db.ProcessedStripeEvents.Add(ProcessedStripeEvent.Create(eventId, FixedNow));
        await _db.SaveChangesAsync();

        var (json, sig) = Sign(BuildUnknownEventJson(eventId));
        await Create().ProcessAsync(json, sig);

        var count = await _db.ProcessedStripeEvents.CountAsync();
        count.Should().Be(1);
    }

    // ── Unknown event type ─────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_UnknownEventType_ReturnsSuccessAndMarksProcessed()
    {
        var eventId = "evt_unknown_type";
        var (json, sig) = Sign(BuildUnknownEventJson(eventId));

        var result = await Create().ProcessAsync(json, sig);

        result.IsSuccess.Should().BeTrue();
        var processed = await _db.ProcessedStripeEvents.FindAsync(eventId);
        processed.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessAsync_UnknownEventType_DoesNotCallAudit()
    {
        var (json, sig) = Sign(BuildUnknownEventJson("evt_no_audit"));

        await Create().ProcessAsync(json, sig);

        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    // ── KAN-77: one customer, an old cancelled subscription and a new paid one ─────────

    [Fact]
    public async Task SubscriptionDeleted_ForTheOldSubscription_DoesNotCancelTheRowFollowingTheNewOne()
    {
        var tenantId = await SeedTenantAsync();
        await SeedSubscriptionAsync(tenantId, "sub_new", "cus_shared");

        var (json, sig) = Sign(BuildSubscriptionDeletedJson("evt_del_old", "sub_old", "cus_shared"));
        (await Create().ProcessAsync(json, sig)).IsSuccess.Should().BeTrue();

        var row = await _db.UserSubscriptions.IgnoreQueryFilters().SingleAsync();
        row.Status.Should().Be(SubscriptionStatus.Active, "the customer paid for sub_new; sub_old ending changes nothing");
        row.StripeSubscriptionId.Should().Be("sub_new");
    }

    [Fact]
    public async Task SubscriptionDeleted_ForTheHeldSubscription_CancelsWithStripesEndDate()
    {
        var tenantId = await SeedTenantAsync();
        await SeedSubscriptionAsync(tenantId, "sub_new", "cus_shared");

        var (json, sig) = Sign(BuildSubscriptionDeletedJson("evt_del_new", "sub_new", "cus_shared", endedAt: 1789000000));
        await Create().ProcessAsync(json, sig);

        var row = await _db.UserSubscriptions.IgnoreQueryFilters().SingleAsync();
        row.Status.Should().Be(SubscriptionStatus.Canceled);
        row.CanceledAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1789000000));
    }

    [Fact]
    public async Task InvoicePaymentFailed_ForAnotherSubscriptionOfTheCustomer_DoesNotMarkPastDue()
    {
        var tenantId = await SeedTenantAsync();
        await SeedSubscriptionAsync(tenantId, "sub_new", "cus_shared");

        var (json, sig) = Sign(BuildInvoiceJson("evt_inv_old", "invoice.payment_failed", "cus_shared", "sub_old"));
        await Create().ProcessAsync(json, sig);

        (await _db.UserSubscriptions.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task InvoicePaymentFailed_ForTheHeldSubscription_MarksPastDue()
    {
        var tenantId = await SeedTenantAsync();
        await SeedSubscriptionAsync(tenantId, "sub_new", "cus_shared");

        var (json, sig) = Sign(BuildInvoiceJson("evt_inv_new", "invoice.payment_failed", "cus_shared", "sub_new"));
        await Create().ProcessAsync(json, sig);

        (await _db.UserSubscriptions.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public async Task InvoicePaymentFailed_WithoutSubscriptionOnThePayload_FallsBackToTheCustomer()
    {
        var tenantId = await SeedTenantAsync();
        await SeedSubscriptionAsync(tenantId, "sub_new", "cus_shared");

        var (json, sig) = Sign(BuildInvoiceJson("evt_inv_legacy", "invoice.payment_failed", "cus_shared", subscriptionId: null));
        await Create().ProcessAsync(json, sig);

        (await _db.UserSubscriptions.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(SubscriptionStatus.PastDue);
    }

    private async Task SeedSubscriptionAsync(Guid tenantId, string subscriptionId, string customerId)
    {
        var sub = UserSubscription.CreatePending(Guid.NewGuid(), tenantId, "t@t.io", FixedNow);
        sub.UpdateFromStripe("pro", SubscriptionStatus.Active, subscriptionId, customerId, "price_pro", "prod_pro",
            FixedNow, FixedNow.AddMonths(1), FixedNow.AddMonths(1), FixedNow);
        _db.UserSubscriptions.Add(sub);
        await _db.SaveChangesAsync();
    }

    private static string BuildSubscriptionDeletedJson(string eventId, string subscriptionId, string customerId, long? endedAt = null)
    {
        var ended = endedAt.HasValue ? ", \"ended_at\": " + endedAt.Value : "";
        return "{ \"id\": \"" + eventId + "\", \"object\": \"event\", \"type\": \"customer.subscription.deleted\", "
             + "\"api_version\": \"2025-03-31.basil\", \"data\": { \"object\": { \"id\": \"" + subscriptionId
             + "\", \"object\": \"subscription\", \"customer\": \"" + customerId + "\", \"status\": \"canceled\"" + ended + " } } }";
    }

    private static string BuildInvoiceJson(string eventId, string type, string customerId, string? subscriptionId)
    {
        var parent = subscriptionId is null
            ? ""
            : ", \"parent\": { \"type\": \"subscription_details\", \"subscription_details\": { \"subscription\": \"" + subscriptionId + "\" } }";
        return "{ \"id\": \"" + eventId + "\", \"object\": \"event\", \"type\": \"" + type + "\", "
             + "\"api_version\": \"2025-03-31.basil\", \"data\": { \"object\": { \"id\": \"in_" + eventId
             + "\", \"object\": \"invoice\", \"customer\": \"" + customerId + "\"" + parent + " } } }";
    }

    // ── Tenant seeding helpers ─────────────────────────────────────────────

    private async Task<Guid> SeedTenantAsync()
    {
        var id = Guid.NewGuid();
        var tenant = Tenant.Create($"T {id:N}", id.ToString("N")[..8], id, FixedNow.UtcDateTime);
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();
        return id;
    }

    // ── Builders and helpers ───────────────────────────────────────────────

    private StripeWebhookService Create() => new(
        _db, _options, Options.Create(new Wasnie.Application.Common.Options.BillingOptions()),
        new Wasnie.Application.Assistant.Common.AssistantPeriodCloser(
            _db, Options.Create(new Wasnie.Application.Common.Options.BillingOptions()), _clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Wasnie.Application.Assistant.Common.AssistantPeriodCloser>.Instance),
        _audit, _clock, TestPlanCatalog.Create(), _logger);

    private static string BuildUnknownEventJson(string eventId) => $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "type": "customer.unknown_event",
          "api_version": "2024-06-20",
          "data": { "object": { "id": "x", "object": "unknown" } }
        }
        """;

    private static (string json, string signature) Sign(string payload)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        return (payload, $"t={timestamp},v1={BitConverter.ToString(hash).Replace("-", "").ToLower()}");
    }

    public void Dispose() => _db.Dispose();
}
