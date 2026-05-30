using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Observability;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class CorrelationIdTests
{
    private readonly TestDatabaseFixture _fixture;

    public CorrelationIdTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Response_AlwaysHasXCorrelationIdHeader()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/auth/me");

        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values!.Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Response_GeneratesGuidCorrelationId_WhenNotProvided()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/auth/me");

        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        Guid.TryParse(values!.First(), out _).Should().BeTrue("generated correlation ID should be a valid GUID");
    }

    [Fact]
    public async Task Response_EchoesClientSuppliedCorrelationId()
    {
        const string clientCorrelationId = "test-correlation-abc-12345";
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", clientCorrelationId);

        var response = await client.GetAsync("/api/auth/me");

        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values!.Should().ContainSingle().Which.Should().Be(clientCorrelationId);
    }

    [Fact]
    public async Task TwoRequests_WithoutCorrelationId_ReceiveDifferentIds()
    {
        var client = _fixture.Factory.CreateClient();

        var response1 = await client.GetAsync("/api/auth/me");
        var response2 = await client.GetAsync("/api/auth/me");

        response1.Headers.TryGetValues("X-Correlation-ID", out var values1).Should().BeTrue();
        response2.Headers.TryGetValues("X-Correlation-ID", out var values2).Should().BeTrue();

        values1!.First().Should().NotBe(values2!.First(), "each request should get its own correlation ID");
    }
}
