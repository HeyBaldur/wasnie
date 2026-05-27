using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Security;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class RateLimitTests
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public RateLimitTests(TestDatabaseFixture fixture)
    {
        _client = fixture.Factory.CreateClient();
    }

    private StringContent LoginBody() =>
        new(JsonSerializer.Serialize(new { email = "test@example.com", password = "P@ssword123!" }),
            Encoding.UTF8, "application/json");

    [Fact]
    public async Task Login_WithinLimit_DoesNotReturn429()
    {
        // TestWebApplicationFactory overrides auth-login PermitLimit to 10000,
        // so a single request must never be rate-limited.
        var response = await _client.PostAsync("/api/auth/login", LoginBody());

        response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task GlobalLimiter_WithinLimit_DoesNotReturn429()
    {
        // TestWebApplicationFactory overrides global PermitLimit to 10000,
        // so a normal sequence of requests must never be rate-limited.
        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    // SKIP: Verifying that the rate limiter actually fires at low limits requires either
    // a test-scoped factory with a very small PermitLimit (1-2) or a way to exhaust
    // a shared partition without flaking on parallel CI runs.  The production behaviour
    // (429 + Retry-After header) is exercised by manual load testing; the unit tests
    // for AddFixedWindowLimiter are owned by Microsoft.AspNetCore.RateLimiting itself.
    // TODO: Revisit when a per-test factory pattern is introduced (WI-14).
    [Fact(Skip = "Requires isolated factory with low PermitLimit — see comment above")]
    public Task Login_ExceedsLimit_Returns429()
    {
        return Task.CompletedTask;
    }

    [Fact(Skip = "Requires isolated factory with low PermitLimit — see comment above")]
    public Task GlobalLimiter_ExceedsLimit_Returns429()
    {
        return Task.CompletedTask;
    }
}
