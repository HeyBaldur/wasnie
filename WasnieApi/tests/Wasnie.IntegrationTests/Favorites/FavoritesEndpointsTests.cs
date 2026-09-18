using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Favorites;

/// <summary>
/// KAN-64 over HTTP and real SQL: the route parses the type, the unique index holds, the real
/// <c>PayeeAccessGuard</c> refuses a Rep, and users and tenants never see each other's stars.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class FavoritesEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _adminA = null!;

    public FavoritesEndpointsTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Favorites");
        }

        await _fixture.ResetPayeesAsync();
        _adminA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Star_ThenList_ReturnsTheResolvedPayee_WithStatusAsAString()
    {
        var payee = await CreatePayeeAsync(_adminA, "Aleksandra Wojcik", "EMP402");

        (await _adminA.PutAsync($"/api/favorites/payee/{payee.Id}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        // Idempotent over real SQL: the unique index is not tripped by a second star.
        (await _adminA.PutAsync($"/api/favorites/Payee/{payee.Id}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var json = await _adminA.GetStringAsync("/api/favorites/payee");
        json.Should().Contain("\"status\":\"Active\"", "the client whitelists status NAMES, so the enum must travel as a string");

        var items = await _adminA.GetFromJsonAsync<List<FavoriteItem>>("/api/favorites/payee");
        items.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new FavoriteItem(payee.Id, "Aleksandra Wojcik", "EMP402", null, "Active"));
    }

    [Fact]
    public async Task Unstar_RemovesIt()
    {
        var payee = await CreatePayeeAsync(_adminA, "Andrea Gomez", "NB-3056");
        await _adminA.PutAsync($"/api/favorites/payee/{payee.Id}", null);

        (await _adminA.DeleteAsync($"/api/favorites/payee/{payee.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _adminA.GetFromJsonAsync<List<FavoriteItem>>("/api/favorites/payee")).Should().BeEmpty();
    }

    [Fact]
    public async Task TwoUsersOfOneTenant_AndAnotherTenant_SeeOnlyTheirOwn()
    {
        var payee = await CreatePayeeAsync(_adminA, "Aleksandra Wojcik", "EMP402");
        await _adminA.PutAsync($"/api/favorites/payee/{payee.Id}", null);

        var otherUser = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserBId);
        (await otherUser.GetFromJsonAsync<List<FavoriteItem>>("/api/favorites/payee")).Should().BeEmpty();

        var otherTenant = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
        (await otherTenant.GetFromJsonAsync<List<FavoriteItem>>("/api/favorites/payee")).Should().BeEmpty();
        // And cannot star tenant A's payee: to tenant B it does not exist.
        (await otherTenant.PutAsync($"/api/favorites/payee/{payee.Id}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// KAN-93 MOVED THIS FROM 404 TO 403, AND THE NEW ANSWER IS THE BETTER ONE. It used to be the
    /// PayeeAccessGuard talking: a Rep could read payees, saw only their own, and a colleague's id came
    /// back as "no such payee". Payees.Read was then removed from the Rep role entirely, so the
    /// permission check now answers first and the guard is never reached — the same shape as the plan
    /// case directly below, which a Rep has never been able to star.
    ///
    /// Distinguishing the two matters: 404 says "not yours", 403 says "this whole surface is not for
    /// you". The second is what is true now.
    /// </summary>
    [Fact]
    public async Task ARep_HasNoPayeesRead_SoStarringAColleagueIsForbidden()
    {
        var payee = await CreatePayeeAsync(_adminA, "Colleague", "EMP777");
        var rep = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserBId, "Rep");

        var response = await rep.PutAsync($"/api/favorites/payee/{payee.Id}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ARep_HasNoPlansRead_SoStarringAPlanIsForbidden()
    {
        var rep = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserBId, "Rep");

        var response = await rep.PutAsync($"/api/favorites/plan/{Guid.NewGuid()}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("transaction")]
    public async Task AnUnknownOrNumericType_IsNotFound(string type)
    {
        (await _adminA.GetAsync($"/api/favorites/{type}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _adminA.PutAsync($"/api/favorites/{type}/{Guid.NewGuid()}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<PayeeResponse> CreatePayeeAsync(HttpClient client, string fullName, string employeeCode)
    {
        var response = await client.PostAsJsonAsync("/api/payees", new
        {
            fullName,
            employeeCode,
            email = $"{employeeCode.ToLower()}@test.com",
            hireDate = "2024-01-01",
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PayeeResponse>())!;
    }

    private sealed record PayeeResponse(Guid Id, string FullName, string EmployeeCode);

    private sealed record FavoriteItem(Guid EntityId, string Name, string? Code, int? Version, string Status);
}
