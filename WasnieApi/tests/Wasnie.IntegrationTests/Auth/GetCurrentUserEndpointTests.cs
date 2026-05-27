using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Auth;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class GetCurrentUserEndpointTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _client = null!;

    public GetCurrentUserEndpointTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserAId, "TenantAdmin");
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetMe_ReturnsOk_ForAuthenticatedUser()
    {
        var response = await _client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMe_ReturnsCorrectUserId()
    {
        var response = await _client.GetAsync("/api/auth/me");
        var dto = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        dto!.UserId.Should().Be(TestConstants.UserAId);
    }

    [Fact]
    public async Task GetMe_ReturnsCorrectRole()
    {
        var response = await _client.GetAsync("/api/auth/me");
        var dto = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        dto!.Role.Should().Be("TenantAdmin");
    }

    [Fact]
    public async Task GetMe_ReturnsPermissions_ForTenantAdmin()
    {
        var response = await _client.GetAsync("/api/auth/me");
        var dto = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        dto!.Permissions.Should().NotBeEmpty();
        dto.Permissions.Should().Contain("Payees.Create");
        dto.Permissions.Should().Contain("Plans.Create");
    }

    [Fact]
    public async Task GetMe_Returns401_WhenUnauthenticated()
    {
        var anonClient = _fixture.Factory.CreateClient();
        var response = await anonClient.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record CurrentUserResponse(
        string UserId,
        string Email,
        string Role,
        Guid TenantId,
        string TenantSlug,
        string Tier,
        List<string> Permissions);
}
