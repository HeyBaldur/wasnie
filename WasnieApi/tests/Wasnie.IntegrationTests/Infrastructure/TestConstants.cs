namespace Wasnie.IntegrationTests.Infrastructure;

internal static class TestConstants
{
    internal const string JwtSecret = "test-secret-key-that-is-at-least-32-characters-long";
    internal const string JwtIssuer = "WasnieApi";
    internal const string JwtAudience = "WasnieUi";

    internal static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    internal static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    internal const string UserAId = "user-a";
    internal const string UserBId = "user-b";

    // Stripe test webhook secret — used to sign synthetic webhook events in tests.
    internal const string StripeWebhookSecret = "whsec_test_webhook_secret_for_integration_tests";
}
