using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Auth;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class AuthEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _client = null!;

    public AuthEndpointsTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetRefreshTokensAsync();
        _client = _fixture.Factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<AuthResultDto> RegisterAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await _client.PostAsJsonAsync("/api/auth/register-tenant", new
        {
            TenantName = $"Test Tenant {suffix}",
            TenantSlug = $"test-{suffix}",
            AdminEmail = $"admin-{suffix}@test.com",
            AdminPassword = "TestPassword!1",
            AdminFirstName = "Admin",
            AdminLastName = "User"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuthResultDto>())!;
    }

    /// <summary>Marks the account confirmed directly — the confirmation email flow is not what this
    /// test is about, and there is no endpoint to confirm without the emailed token.</summary>
    private async Task ConfirmEmailAsync(string email)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE AspNetUsers SET EmailConfirmed = 1 WHERE NormalizedEmail = {email.ToUpperInvariant()}");
    }

    private async Task<AuthResultDto> LoginAsync(string email, string password = "TestPassword!1")
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuthResultDto>())!;
    }

    // ── F-012: Logout revokes refresh tokens ──────────────────────────────────

    [Fact]
    public async Task Logout_WithValidToken_RevokesRefreshToken_SubsequentRefreshReturns401()
    {
        var auth = await RegisterAsync();
        // Register returns a token signed with the app secret; for logout we need a token
        // signed with the test secret. Use the real userId so revocation matches the DB rows.
        _client.WithAuth(auth.TenantId, auth.UserId);

        var logoutResponse = await _client.PostAsync("/api/auth/logout", null);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        _client.DefaultRequestHeaders.Authorization = null;
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { RefreshToken = auth.Tokens.RefreshToken });
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RevokesAllActiveRefreshTokensForUser()
    {
        var auth1 = await RegisterAsync();
        // Logging in requires a confirmed email since WI-EMAIL-ACTIVATION (2026-06-15); a freshly
        // registered admin has not confirmed one, so without this the second login 401s and the test
        // dies in its own setup, never reaching the logout it exists to verify.
        await ConfirmEmailAsync(auth1.Email);
        // Second login produces a second refresh token for the same user
        var auth2 = await LoginAsync(auth1.Email);

        _client.WithAuth(auth1.TenantId, auth1.UserId);
        var logoutResponse = await _client.PostAsync("/api/auth/logout", null);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        _client.DefaultRequestHeaders.Authorization = null;
        var refresh1 = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { RefreshToken = auth1.Tokens.RefreshToken });
        var refresh2 = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { RefreshToken = auth2.Tokens.RefreshToken });

        refresh1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refresh2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WhenNoActiveRefreshTokensExist_Returns204()
    {
        var auth = await RegisterAsync();
        _client.WithAuth(auth.TenantId, auth.UserId);

        // First logout revokes the token
        var first = await _client.PostAsync("/api/auth/logout", null);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Second logout — no active tokens remain — still succeeds
        var second = await _client.PostAsync("/api/auth/logout", null);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── F-015: Refresh token validator ────────────────────────────────────────

    [Fact]
    public async Task Refresh_WithEmptyToken_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_WithWhitespaceOnlyToken_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = "   " });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_WithTokenTooShort_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = "tooshort" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
