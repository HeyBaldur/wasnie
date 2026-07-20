using FluentAssertions;
using Wasnie.Domain.Integrations.HubSpot;

namespace Wasnie.UnitTests.Integrations;

public sealed class HubSpotConnectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);

    private static HubSpotConnection Connected() =>
        HubSpotConnection.Create(
            Guid.NewGuid(), Guid.NewGuid(), 42L, "enc-access", "enc-refresh",
            Now.AddMinutes(30), "user-1", Now);

    [Fact]
    public void Create_starts_Connected_with_no_reason()
    {
        var c = Connected();
        c.Status.Should().Be(HubSpotConnectionStatus.Connected);
        c.StatusReason.Should().BeNull();
        c.PortalId.Should().Be(42L);
    }

    [Fact]
    public void ApplyRefreshedTokens_replaces_tokens_and_stays_connected()
    {
        var c = Connected();
        c.ApplyRefreshedTokens("enc-access-2", "enc-refresh-2", Now.AddMinutes(60), Now.AddMinutes(1));

        c.AccessTokenEncrypted.Should().Be("enc-access-2");
        c.RefreshTokenEncrypted.Should().Be("enc-refresh-2");
        c.Status.Should().Be(HubSpotConnectionStatus.Connected);
    }

    [Fact]
    public void MarkNeedsReconnect_sets_status_and_reason()
    {
        var c = Connected();
        c.MarkNeedsReconnect("refresh token revoked", Now.AddMinutes(5));

        c.Status.Should().Be(HubSpotConnectionStatus.NeedsReconnect);
        c.StatusReason.Should().Be("refresh token revoked");
    }

    [Fact]
    public void Disconnect_clears_tokens_and_records_who_and_when()
    {
        var c = Connected();
        c.Disconnect("Disconnected by user.", "user-2", Now.AddMinutes(10));

        c.Status.Should().Be(HubSpotConnectionStatus.Disconnected);
        c.AccessTokenEncrypted.Should().BeEmpty();
        c.RefreshTokenEncrypted.Should().BeEmpty();
        c.DisconnectedBy.Should().Be("user-2");
        c.DisconnectedAt.Should().Be(Now.AddMinutes(10));
        c.StatusReason.Should().Be("Disconnected by user.");
    }

    [Fact]
    public void Reconnect_restores_connected_state_and_clears_disconnect_fields()
    {
        var c = Connected();
        c.Disconnect("Disconnected by user.", "user-2", Now.AddMinutes(10));

        c.Reconnect(99L, "enc-a3", "enc-r3", Now.AddMinutes(40), "user-3", Now.AddMinutes(20));

        c.Status.Should().Be(HubSpotConnectionStatus.Connected);
        c.PortalId.Should().Be(99L);
        c.AccessTokenEncrypted.Should().Be("enc-a3");
        c.DisconnectedAt.Should().BeNull();
        c.DisconnectedBy.Should().BeNull();
        c.StatusReason.Should().BeNull();
    }

    [Fact]
    public void OAuthState_is_valid_until_used_or_expired()
    {
        var state = HubSpotOAuthState.Create(Guid.NewGuid(), Guid.NewGuid(), "user-1", Now, TimeSpan.FromMinutes(10));

        state.IsValid(Now.AddMinutes(5)).Should().BeTrue();
        state.IsValid(Now.AddMinutes(11)).Should().BeFalse(); // expired

        state.MarkUsed(Now.AddMinutes(2));
        state.IsValid(Now.AddMinutes(3)).Should().BeFalse(); // already used
    }
}
