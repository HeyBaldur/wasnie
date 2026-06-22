using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Integrations.HubSpot;
using Wasnie.Domain.Integrations.HubSpot;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services.HubSpot;

namespace Wasnie.UnitTests.Integrations;

public sealed class HubSpotTokenProviderTests : IDisposable
{
    private const string Key = "vLGER65fH1O7Nt7nbwTTC2FKKUJ+hMADby9FwcmxihE=";
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly AesTokenEncryptionService _enc;
    private readonly IHubSpotOAuthClient _client = Substitute.For<IHubSpotOAuthClient>();
    private readonly HubSpotTokenProvider _provider;

    public HubSpotTokenProviderTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        _enc = new AesTokenEncryptionService(Options.Create(new HubSpotOptions { TokenEncryptionKey = Key }));

        var clock = Substitute.For<IClock>();
        clock.UtcNowOffset.Returns(Now);

        var audit = Substitute.For<IAuditService>();
        audit.LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);

        var options = Options.Create(new HubSpotOptions { TokenEncryptionKey = Key, RefreshSkewSeconds = 300 });

        _provider = new HubSpotTokenProvider(_db, _client, _enc, audit, clock, options);
    }

    public void Dispose() => _db.Dispose();

    private HubSpotConnection Seed(DateTimeOffset expiresAt, string access = "old-access", string refresh = "old-refresh")
    {
        var conn = HubSpotConnection.Create(
            Guid.NewGuid(), TenantId, 7L, _enc.Encrypt(access), _enc.Encrypt(refresh), expiresAt, "user-1", Now);
        _db.HubSpotConnections.Add(conn);
        _db.SaveChanges();
        return conn;
    }

    [Fact]
    public async Task Returns_current_token_when_not_expired_without_refreshing()
    {
        Seed(expiresAt: Now.AddHours(1));

        var token = await _provider.GetValidAccessTokenAsync(TenantId);

        token.Should().Be("old-access");
        await _client.DidNotReceive().RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refreshes_when_expired_and_persists_new_tokens_discarding_old()
    {
        Seed(expiresAt: Now.AddMinutes(-1)); // expired
        _client.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(HubSpotRefreshResult.Ok(new HubSpotTokenResult("new-access", "new-refresh", 1800)));

        var token = await _provider.GetValidAccessTokenAsync(TenantId);

        token.Should().Be("new-access");

        var stored = await _db.HubSpotConnections.IgnoreQueryFilters().FirstAsync();
        _enc.Decrypt(stored.AccessTokenEncrypted).Should().Be("new-access");
        _enc.Decrypt(stored.RefreshTokenEncrypted).Should().Be("new-refresh");
        stored.Status.Should().Be(HubSpotConnectionStatus.Connected);
    }

    [Fact]
    public async Task Bad_refresh_token_moves_connection_to_NeedsReconnect_and_returns_null()
    {
        Seed(expiresAt: Now.AddMinutes(-1));
        _client.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(HubSpotRefreshResult.Invalid("BAD_REFRESH_TOKEN"));

        var token = await _provider.GetValidAccessTokenAsync(TenantId);

        token.Should().BeNull();

        var stored = await _db.HubSpotConnections.IgnoreQueryFilters().FirstAsync();
        stored.Status.Should().Be(HubSpotConnectionStatus.NeedsReconnect);
        stored.StatusReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Returns_null_for_disconnected_connection_without_calling_hubspot()
    {
        var conn = Seed(expiresAt: Now.AddHours(1));
        conn.Disconnect("Disconnected by user.", "user-1", Now);
        await _db.SaveChangesAsync();

        var token = await _provider.GetValidAccessTokenAsync(TenantId);

        token.Should().BeNull();
        await _client.DidNotReceive().RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_null_when_no_connection_exists()
    {
        (await _provider.GetValidAccessTokenAsync(TenantId)).Should().BeNull();
    }
}
