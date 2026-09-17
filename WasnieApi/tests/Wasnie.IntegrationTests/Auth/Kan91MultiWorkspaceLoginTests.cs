using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Identity;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Auth;

/// <summary>
/// KAN-91, end to end: one account in two workspaces, and the sign-in that tells them apart.
///
/// ★★ IT HAD TO BE AN INTEGRATION TEST, and the attempt that proved it is worth recording. The first
/// verification was to sign in as a real account against the dev database — which cannot work: the
/// only honest way to check a password is to know it, and inventing or resetting one on somebody's
/// real account is not something a test may do. This suite registers its own tenants with credentials
/// it chose, which is the only way to exercise the WHOLE path: HTTP in, token out.
///
/// ★★ AND THE TOKEN IS WHAT IS ASSERTED, not the database. Everything downstream of sign-in reads the
/// JWT — <c>ITenantContext</c>, <c>AuthorizationService</c>, every query filter — so a test that
/// checked the membership rows would be checking the input and calling it the output (§A2). These
/// read the tenant and the role the caller actually received.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class Kan91MultiWorkspaceLoginTests : IAsyncLifetime
{
    private const string Password = "TestPassword!1";

    private readonly TestDatabaseFixture _fixture;
    private HttpClient _client = null!;

    public Kan91MultiWorkspaceLoginTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetRefreshTokensAsync();
        _client = _fixture.Factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Harness ───────────────────────────────────────────────────────────────

    private sealed record Workspace(Guid TenantId, string Slug, string AdminEmail);

    private async Task<Workspace> RegisterWorkspaceAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"ws-{suffix}";
        var email = $"admin-{suffix}@test.com";

        var response = await _client.PostAsJsonAsync("/api/auth/register-tenant", new
        {
            TenantName = $"Workspace {suffix}",
            TenantSlug = slug,
            AdminEmail = email,
            AdminPassword = Password,
            AdminFirstName = "Admin",
            AdminLastName = "User",
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var auth = (await response.Content.ReadFromJsonAsync<AuthResultDto>())!;
        await ConfirmAsync(email);
        return new Workspace(auth.TenantId, slug, email);
    }

    private async Task ConfirmAsync(string email)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE AspNetUsers SET EmailConfirmed = 1 WHERE NormalizedEmail = {email.ToUpperInvariant()}");
    }

    /// <summary>
    /// Gives an existing account a membership of another workspace — what accepting an invitation
    /// does, without going through the email.
    /// </summary>
    private async Task AddMembershipAsync(string email, Guid tenantId, string role)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var userId = await db.Database
            .SqlQuery<string>($"SELECT Id AS Value FROM AspNetUsers WHERE NormalizedEmail = {email.ToUpperInvariant()}")
            .SingleAsync();

        db.TenantUsers.Add(TenantUser.Create(
            Guid.NewGuid(), tenantId, userId, role, null, null, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> PostLoginAsync(string email, string? organizationId = null) =>
        await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password,
            OrganizationId = organizationId,
        });

    private static async Task<string> MessageOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return body;
    }

    // ── The gate: nothing that worked before may stop working ─────────────────

    /// <summary>
    /// ★★ THE ONE THIS TICKET COULD NOT SHIP WITHOUT. Every account in existence belongs to exactly
    /// one workspace, and sign-in now resolves through memberships instead of the claim. If that
    /// resolution were wrong, every customer would be locked out and nobody could fix it from inside
    /// the product. This is that case: one membership, no identifier, straight in.
    /// </summary>
    [Fact]
    public async Task An_account_in_one_workspace_signs_in_with_no_identifier_exactly_as_before()
    {
        var ws = await RegisterWorkspaceAsync();

        var response = await PostLoginAsync(ws.AdminEmail);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResultDto>())!;
        auth.TenantId.Should().Be(ws.TenantId);
        auth.TenantSlug.Should().Be(ws.Slug);
        auth.Roles.Should().ContainSingle().Which.Should().Be(Roles.TenantAdmin);
    }

    // ── The new behaviour ─────────────────────────────────────────────────────

    [Fact]
    public async Task An_account_in_two_workspaces_is_asked_which_one()
    {
        var first = await RegisterWorkspaceAsync();
        var second = await RegisterWorkspaceAsync();
        await AddMembershipAsync(first.AdminEmail, second.TenantId, Roles.Rep);

        var response = await PostLoginAsync(first.AdminEmail);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await MessageOf(response)).Should().Contain("ORGANIZATION_REQUIRED");
    }

    [Fact]
    public async Task The_identifier_decides_which_workspace_the_session_is_for()
    {
        var first = await RegisterWorkspaceAsync();
        var second = await RegisterWorkspaceAsync();
        await AddMembershipAsync(first.AdminEmail, second.TenantId, Roles.Rep);

        var intoFirst = await PostLoginAsync(first.AdminEmail, first.Slug);
        var intoSecond = await PostLoginAsync(first.AdminEmail, second.Slug);

        intoFirst.StatusCode.Should().Be(HttpStatusCode.OK);
        intoSecond.StatusCode.Should().Be(HttpStatusCode.OK);

        var a = (await intoFirst.Content.ReadFromJsonAsync<AuthResultDto>())!;
        var b = (await intoSecond.Content.ReadFromJsonAsync<AuthResultDto>())!;

        a.TenantId.Should().Be(first.TenantId);
        b.TenantId.Should().Be(second.TenantId);
    }

    /// <summary>
    /// ★★ THE AUTHORISATION HOLE THIS TICKET EXISTS TO CLOSE. Identity's roles carry no tenant, so
    /// before KAN-91 this person would have arrived in the second workspace as an administrator
    /// because the FIRST one made them an administrator. The role now travels with the membership.
    /// </summary>
    [Fact]
    public async Task The_role_does_not_leak_from_one_workspace_into_the_other()
    {
        var admin = await RegisterWorkspaceAsync();
        var other = await RegisterWorkspaceAsync();
        await AddMembershipAsync(admin.AdminEmail, other.TenantId, Roles.Rep);

        var asAdmin = (await (await PostLoginAsync(admin.AdminEmail, admin.Slug))
            .Content.ReadFromJsonAsync<AuthResultDto>())!;
        var asRep = (await (await PostLoginAsync(admin.AdminEmail, other.Slug))
            .Content.ReadFromJsonAsync<AuthResultDto>())!;

        asAdmin.Roles.Should().ContainSingle().Which.Should().Be(Roles.TenantAdmin);
        asRep.Roles.Should().ContainSingle().Which.Should().Be(Roles.Rep);
    }

    // ── The security property ─────────────────────────────────────────────────

    /// <summary>
    /// ★★ THE PROPERTY THAT MATTERS: an organization that DOES NOT EXIST and one the account is NOT IN
    /// answer identically. Without it, somebody holding a single password could walk the slug space
    /// and enumerate every company using Incentra, and with one address, which of them a person works
    /// for.
    ///
    /// ★ IT IS DELIBERATELY NOT COMPARED WITH A WRONG PASSWORD. The password is verified BEFORE any of
    /// this, so whoever reaches here already holds the account; telling them their own identifier did
    /// not match discloses nothing to a stranger. An earlier version of this test asserted the
    /// stronger property and was wrong about which one protects anything.
    /// </summary>
    [Fact]
    public async Task An_unknown_organization_and_someone_elses_answer_identically()
    {
        var mine = await RegisterWorkspaceAsync();
        var somebodyElses = await RegisterWorkspaceAsync();

        var doesNotExist = await PostLoginAsync(mine.AdminEmail, "no-such-organization-at-all");
        var existsButNotMine = await PostLoginAsync(mine.AdminEmail, somebodyElses.Slug);

        doesNotExist.StatusCode.Should().Be(existsButNotMine.StatusCode);
        (await MessageOf(doesNotExist)).Should().Be(await MessageOf(existsButNotMine));

        // And neither answer names the workspace that was probed.
        (await MessageOf(existsButNotMine)).Should().NotContain(somebodyElses.Slug);
    }

    /// <summary>
    /// ★★ A MISTYPED IDENTIFIER IS REFUSED EVEN WHEN THE ACCOUNT HAS ONLY ONE WORKSPACE. The first
    /// implementation only checked the identifier when there was more than one membership, so a person
    /// with a single workspace who typed the wrong organization was signed into theirs anyway — the
    /// product ignoring what they had just told it. An integration test caught it; this is that test.
    /// </summary>
    [Fact]
    public async Task A_wrong_identifier_is_refused_even_with_a_single_workspace()
    {
        var mine = await RegisterWorkspaceAsync();

        var response = await PostLoginAsync(mine.AdminEmail, "not-my-organization");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "typing the wrong organization must never sign somebody in somewhere else");
    }

    // ── Deactivation, now that it is per workspace ────────────────────────────

    [Fact]
    public async Task Being_switched_off_in_one_workspace_does_not_close_the_other()
    {
        var first = await RegisterWorkspaceAsync();
        var second = await RegisterWorkspaceAsync();
        await AddMembershipAsync(first.AdminEmail, second.TenantId, Roles.Rep);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlAsync(
                $"UPDATE TenantUsers SET DeactivatedAt = SYSDATETIMEOFFSET() WHERE TenantId = {first.TenantId}");
        }

        var intoClosed = await PostLoginAsync(first.AdminEmail, first.Slug);
        var intoOpen = await PostLoginAsync(first.AdminEmail, second.Slug);

        intoClosed.StatusCode.Should().NotBe(HttpStatusCode.OK);
        intoOpen.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
