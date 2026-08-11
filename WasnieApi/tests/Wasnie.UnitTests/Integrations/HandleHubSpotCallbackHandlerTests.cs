using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Integrations.HubSpot;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Entities;
using Wasnie.Domain.Integrations.HubSpot;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services.HubSpot;

namespace Wasnie.UnitTests.Integrations;

public sealed class HandleHubSpotCallbackHandlerTests : IDisposable
{
    private const string Key = "vLGER65fH1O7Nt7nbwTTC2FKKUJ+hMADby9FwcmxihE=";
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly AesTokenEncryptionService _enc;
    private readonly IHubSpotOAuthClient _client = Substitute.For<IHubSpotOAuthClient>();
    private readonly HandleHubSpotCallbackHandler _handler;

    public HandleHubSpotCallbackHandlerTests()
    {
        // Anonymous callback → no ambient tenant.
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(Guid.Empty);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        _enc = new AesTokenEncryptionService(Options.Create(new HubSpotOptions { TokenEncryptionKey = Key }));

        var clock = Substitute.For<IClock>();
        clock.UtcNowOffset.Returns(Now);

        var guid = Substitute.For<IGuidGenerator>();
        guid.NewGuid().Returns(_ => Guid.NewGuid());

        var audit = Substitute.For<IAuditService>();
        audit.LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var options = Options.Create(new HubSpotOptions
        {
            ClientId = "cid",
            ClientSecret = "secret",
            TokenEncryptionKey = Key,
        });

        _handler = new HandleHubSpotCallbackHandler(
            _db, _client, _enc, audit, clock, guid, options,
            NullLogger<HandleHubSpotCallbackHandler>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // The callback re-checks the plan from the STATE's tenant (it is anonymous — there is no ambient
    // tenant), so the handshake only completes for a tenant whose row says it is paid.
    private void SeedTenant(Tier tier = Tier.Growth)
    {
        if (_db.Tenants.IgnoreQueryFilters().Any(t => t.Id == TenantId))
            return;

        var tenant = Tenant.Create("Acme", $"acme-{TenantId:N}", TenantId, Now);
        tenant.SetTier(tier);
        _db.Tenants.Add(tenant);
        _db.SaveChanges();
    }

    private Guid SeedState(DateTimeOffset? now = null)
    {
        SeedTenant();
        var state = HubSpotOAuthState.Create(
            Guid.NewGuid(), TenantId, "user-1", now ?? Now, TimeSpan.FromMinutes(10));
        _db.HubSpotOAuthStates.Add(state);
        _db.SaveChanges();
        return state.Id;
    }

    [Fact]
    public async Task Valid_callback_exchanges_code_and_persists_encrypted_connection()
    {
        var stateId = SeedState();
        _client.ExchangeCodeAsync("the-code", Arg.Any<CancellationToken>())
               .Returns(new HubSpotTokenResult("access-xyz", "refresh-xyz", 1800));
        _client.GetPortalIdAsync("access-xyz", Arg.Any<CancellationToken>()).Returns(123456L);

        var result = await _handler.Handle(new HandleHubSpotCallbackCommand("the-code", stateId.ToString()), default);

        result.IsSuccess.Should().BeTrue();

        var conn = await _db.HubSpotConnections.IgnoreQueryFilters().SingleAsync();
        conn.TenantId.Should().Be(TenantId);
        conn.PortalId.Should().Be(123456L);
        conn.Status.Should().Be(HubSpotConnectionStatus.Connected);

        // Tokens are stored ENCRYPTED — not as plaintext.
        conn.AccessTokenEncrypted.Should().NotBe("access-xyz");
        conn.RefreshTokenEncrypted.Should().NotBe("refresh-xyz");
        _enc.Decrypt(conn.AccessTokenEncrypted).Should().Be("access-xyz");
        _enc.Decrypt(conn.RefreshTokenEncrypted).Should().Be("refresh-xyz");

        // State is consumed (one-time use).
        var state = await _db.HubSpotOAuthStates.IgnoreQueryFilters().SingleAsync();
        state.IsValid(Now).Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_state_is_rejected_and_creates_no_connection()
    {
        var result = await _handler.Handle(
            new HandleHubSpotCallbackCommand("the-code", Guid.NewGuid().ToString()), default);

        result.IsSuccess.Should().BeFalse();
        (await _db.HubSpotConnections.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        await _client.DidNotReceive().ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expired_state_is_rejected()
    {
        // Created 20 minutes ago with a 10-minute TTL → expired by Now.
        var stateId = SeedState(now: Now.AddMinutes(-20));

        var result = await _handler.Handle(new HandleHubSpotCallbackCommand("the-code", stateId.ToString()), default);

        result.IsSuccess.Should().BeFalse();
        await _client.DidNotReceive().ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconnect_updates_the_existing_row_for_the_tenant()
    {
        // Pre-existing disconnected connection for the same tenant.
        var existing = HubSpotConnection.Create(
            Guid.NewGuid(), TenantId, 1L, _enc.Encrypt("a"), _enc.Encrypt("b"), Now, "user-0", Now);
        existing.Disconnect("by user", "user-0", Now);
        _db.HubSpotConnections.Add(existing);
        _db.SaveChanges();

        var stateId = SeedState();
        _client.ExchangeCodeAsync("c", Arg.Any<CancellationToken>())
               .Returns(new HubSpotTokenResult("a2", "b2", 1800));
        _client.GetPortalIdAsync("a2", Arg.Any<CancellationToken>()).Returns(2L);

        var result = await _handler.Handle(new HandleHubSpotCallbackCommand("c", stateId.ToString()), default);

        result.IsSuccess.Should().BeTrue();
        var all = await _db.HubSpotConnections.IgnoreQueryFilters().ToListAsync();
        all.Should().ContainSingle(); // reused, not duplicated
        all[0].Status.Should().Be(HubSpotConnectionStatus.Connected);
        all[0].PortalId.Should().Be(2L);
    }
}
