using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Subscription;

namespace Wasnie.IntegrationTests.Infrastructure;

/// <summary>
/// KAN-77 — a THREE-plan catalog with caps, for the tests whose subject is plan limits, checkout blocking and plan
/// changes. The shipped configuration has one unlimited plan, where those paths are dormant; registering this
/// catalog through <c>WithWebHostBuilder</c> exercises them exactly as a future multi-plan configuration would.
/// </summary>
public static class TestPlans
{
    public static ISubscriptionPlanCatalog ThreePlanCatalog() =>
        new SubscriptionPlanCatalog(
            Options.Create(new BillingOptions
            {
                DefaultPlanCode = "scale",
                Plans =
                [
                    new SubscriptionPlanDefinition { Code = "starter", MaxPayees = 25, MaxPlans = 5 },
                    new SubscriptionPlanDefinition { Code = "growth", MaxPayees = 75, MaxPlans = 15 },
                    new SubscriptionPlanDefinition { Code = "scale", MaxPayees = 150, MaxPlans = null },
                ],
            }),
            NullLogger<SubscriptionPlanCatalog>.Instance);
}
