namespace Wasnie.Domain.Subscription;

public enum SubscriptionStatus
{
    Active = 0,
    PastDue = 1,
    Canceled = 2,
    Incomplete = 3,
    Trialing = 4,
}
