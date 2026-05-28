using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Security;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class SecurityHeadersTests
{
    private readonly HttpClient _client;

    public SecurityHeadersTests(TestDatabaseFixture fixture)
    {
        _client = fixture.Factory.CreateClient();
    }

    private async Task<HttpResponseMessage> GetAnyResponseAsync()
    {
        // Use a known unauthenticated endpoint to trigger the middleware pipeline.
        // 401 is fine — we only care about response headers.
        return await _client.GetAsync("/api/auth/me");
    }

    [Fact]
    public async Task Response_HasXFrameOptions_Deny()
    {
        var response = await GetAnyResponseAsync();

        response.Headers.TryGetValues("X-Frame-Options", out var values).Should().BeTrue();
        values!.Should().ContainSingle().Which.Should().Be("DENY");
    }

    [Fact]
    public async Task Response_HasXContentTypeOptions_NoSniff()
    {
        var response = await GetAnyResponseAsync();

        response.Headers.TryGetValues("X-Content-Type-Options", out var values).Should().BeTrue();
        values!.Should().ContainSingle().Which.Should().Be("nosniff");
    }

    [Fact]
    public async Task Response_HasReferrerPolicy()
    {
        var response = await GetAnyResponseAsync();

        response.Headers.TryGetValues("Referrer-Policy", out var values).Should().BeTrue();
        values!.Should().ContainSingle().Which.Should().Be("strict-origin-when-cross-origin");
    }

    [Fact]
    public async Task Response_HasPermissionsPolicy()
    {
        var response = await GetAnyResponseAsync();

        response.Headers.TryGetValues("Permissions-Policy", out var values).Should().BeTrue();
        values!.Should().ContainSingle().Which.Should().Contain("geolocation=()");
    }

    [Fact]
    public async Task Response_HasContentSecurityPolicy_WithFrameAncestorsNone()
    {
        var response = await GetAnyResponseAsync();

        response.Headers.TryGetValues("Content-Security-Policy", out var values).Should().BeTrue();
        values!.Should().ContainSingle().Which.Should().Contain("frame-ancestors 'none'");
    }
}
