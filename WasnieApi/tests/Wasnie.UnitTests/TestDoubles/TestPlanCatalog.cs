using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Subscription;

namespace Wasnie.UnitTests.TestDoubles;

/// <summary>
/// The real <see cref="SubscriptionPlanCatalog"/> over an explicit plan list (KAN-77). Defaults to the shipped
/// shape — one unlimited plan "pro" — so tests read the same catalog the application runs with.
/// </summary>
public static class TestPlanCatalog
{
    public static SubscriptionPlanCatalog Create(params SubscriptionPlanDefinition[] plans) =>
        new(Options.Create(new BillingOptions
            {
                DefaultPlanCode = plans.Length == 0 ? "pro" : plans[0].Code,
                Plans = plans.Length == 0 ? [new SubscriptionPlanDefinition { Code = "pro" }] : plans.ToList(),
            }),
            NullLogger<SubscriptionPlanCatalog>.Instance);
}
