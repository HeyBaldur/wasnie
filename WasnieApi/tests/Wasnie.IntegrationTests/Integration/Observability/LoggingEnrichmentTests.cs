using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Serilog.Events;
using Serilog.Parsing;
using Wasnie.Api.Observability;

namespace Wasnie.IntegrationTests.Integration.Observability;

// Unit-style tests for TenantUserCorrelationEnricher.
// Placed in IntegrationTests so the project reference to Wasnie.Api is available.
public sealed class LoggingEnrichmentTests
{
    [Fact]
    public void Enricher_AddsCorrelationId_WhenPresentInHttpContextItems()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items["CorrelationId"] = "abc-correlation-123";

        var enricher = new TenantUserCorrelationEnricher(new FakeHttpContextAccessor(httpContext));
        var logEvent = MakeLogEvent();

        enricher.Enrich(logEvent, new ScalarPropertyFactory());

        logEvent.Properties.Should().ContainKey("CorrelationId")
            .WhoseValue.Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("abc-correlation-123");
    }

    [Fact]
    public void Enricher_AddsTenantIdAndUserId_FromAuthenticatedUser()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items["CorrelationId"] = "cid-001";

        var claims = new[]
        {
            new Claim("tenant_id", "tenant-aaa"),
            new Claim(ClaimTypes.NameIdentifier, "user-bbb"),
        };
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new ClaimsIdentity(claims, "Test"));

        var enricher = new TenantUserCorrelationEnricher(new FakeHttpContextAccessor(httpContext));
        var logEvent = MakeLogEvent();

        enricher.Enrich(logEvent, new ScalarPropertyFactory());

        logEvent.Properties.Should().ContainKey("TenantId")
            .WhoseValue.Should().BeOfType<ScalarValue>().Which.Value.Should().Be("tenant-aaa");
        logEvent.Properties.Should().ContainKey("UserId")
            .WhoseValue.Should().BeOfType<ScalarValue>().Which.Value.Should().Be("user-bbb");
    }

    [Fact]
    public void Enricher_AddsNoProperties_WhenHttpContextIsNull()
    {
        var enricher = new TenantUserCorrelationEnricher(new FakeHttpContextAccessor(null));
        var logEvent = MakeLogEvent();

        enricher.Enrich(logEvent, new ScalarPropertyFactory());

        logEvent.Properties.Should().BeEmpty();
    }

    [Fact]
    public void Enricher_SkipsCorrelationId_WhenNotInItems()
    {
        var httpContext = new DefaultHttpContext();
        // No CorrelationId in Items

        var enricher = new TenantUserCorrelationEnricher(new FakeHttpContextAccessor(httpContext));
        var logEvent = MakeLogEvent();

        enricher.Enrich(logEvent, new ScalarPropertyFactory());

        logEvent.Properties.Should().NotContainKey("CorrelationId");
    }

    // Helpers

    private static LogEvent MakeLogEvent() =>
        new(DateTimeOffset.UtcNow, LogEventLevel.Information, null,
            new MessageTemplateParser().Parse("test message"),
            Enumerable.Empty<LogEventProperty>());

    private sealed class FakeHttpContextAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class ScalarPropertyFactory : Serilog.Core.ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }
}
