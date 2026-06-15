using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Wasnie.Infrastructure.Services;

namespace Wasnie.UnitTests.Services;

public sealed class StripeProductMappingTests
{
    private readonly ILogger _logger = Substitute.For<ILogger>();

    [Fact]
    public void ResolveTier_ProductInConfigMap_ReturnsTierFromConfig()
    {
        var map = new Dictionary<string, string> { ["prod_starter_abc"] = "Starter" };

        var result = StripeSubscriptionPlanService.ResolveTier(
            "prod_starter_abc",
            new Dictionary<string, string>(),
            map,
            _logger);

        result.Should().Be("Starter");
    }

    [Fact]
    public void ResolveTier_ProductInMetadata_ReturnsTierFromMetadata()
    {
        var metadata = new Dictionary<string, string> { ["tier"] = "Growth" };

        var result = StripeSubscriptionPlanService.ResolveTier(
            "prod_growth_abc",
            metadata,
            new Dictionary<string, string>(),
            _logger);

        result.Should().Be("Growth");
    }

    [Fact]
    public void ResolveTier_MetadataTakesPrecedenceOverConfigMap()
    {
        // Both sources claim a different tier — metadata wins.
        var metadata = new Dictionary<string, string> { ["tier"] = "Scale" };
        var map = new Dictionary<string, string> { ["prod_abc"] = "Starter" };

        var result = StripeSubscriptionPlanService.ResolveTier(
            "prod_abc",
            metadata,
            map,
            _logger);

        result.Should().Be("Scale");
    }

    [Fact]
    public void ResolveTier_NotInMapOrMetadata_ReturnsNull()
    {
        var result = StripeSubscriptionPlanService.ResolveTier(
            "prod_unknown_xyz",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            _logger);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveTier_NotInMapOrMetadata_LogsWarning()
    {
        StripeSubscriptionPlanService.ResolveTier(
            "prod_unknown_xyz",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            _logger);

        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void ResolveTier_EmptyMetadataTierValue_FallsBackToConfigMap()
    {
        // Metadata has the key but the value is blank — treat as absent, use config.
        var metadata = new Dictionary<string, string> { ["tier"] = "   " };
        var map = new Dictionary<string, string> { ["prod_abc"] = "Growth" };

        var result = StripeSubscriptionPlanService.ResolveTier(
            "prod_abc",
            metadata,
            map,
            _logger);

        result.Should().Be("Growth");
    }
}
