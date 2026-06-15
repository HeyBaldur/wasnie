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
            ProductTierMap = new Dictionary<string, string> { ["prod_starter"] = "Starter" },
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

    private StripeWebhookService Create() => new(_db, _options, _audit, _clock, _logger);

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
