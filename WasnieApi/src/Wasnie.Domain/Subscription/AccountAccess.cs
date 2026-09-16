namespace Wasnie.Domain.Subscription;

/// <summary>Whether a tenant can use the product right now (KAN-77).</summary>
public enum AccountAccessState
{
    /// <summary>Inside the free trial: full access, no card asked.</summary>
    Trial,

    /// <summary>Paying through Stripe (Active, or PastDue while Stripe retries the charge).</summary>
    Active,

    /// <summary>The paywall: no access until they subscribe. Data is kept, nothing is deleted.</summary>
    Locked,
}

/// <summary>Why a tenant is <see cref="AccountAccessState.Locked"/>. Null for every other state.</summary>
public enum AccountLockReason
{
    /// <summary>The trial ran out and they never subscribed.</summary>
    TrialEnded,

    /// <summary>They had a Stripe subscription and it is no longer active (canceled, incomplete…).</summary>
    SubscriptionEnded,

    /// <summary>No trial and no subscription at all. Only reachable through data neither registration nor the
    /// KAN-77 migration produce — named on its own so it is never mistaken for an expired trial (§B3).</summary>
    NoSubscription,
}

public sealed record AccountAccess(
    AccountAccessState State,
    AccountLockReason? LockReason,
    DateTimeOffset? TrialEndsAt,
    int? TrialDaysRemaining)
{
    public bool HasAccess => State != AccountAccessState.Locked;
}

/// <summary>
/// ★ THE ONE RULE that turns a tenant's facts into its access. Every reader — the paywall middleware, the
/// metered-feature gate, the HubSpot sync, the assistant's trial allowance, the account endpoint — goes
/// through <see cref="Resolve"/>, so they cannot disagree about who is in.
///
/// ★ DERIVED, NEVER STORED (§B5). No "IsLocked" flag exists to fall out of sync: the trial end date and the
/// subscription are facts, and the state is computed from them on every read. A payment arriving through
/// the webhook unlocks the account on the very next request, with nothing to reset.
///
/// ★★ A STRIPE SUBSCRIPTION ENDS THE TRIAL QUESTION. Once a tenant has subscribed, what matters is whether
/// that subscription is live; the trial days they did not use are not handed back when they cancel. That is
/// what makes "canceled → paywall" true even for someone who subscribed on day two.
///
/// ★ "PAYING" NEEDS A STRIPE SUBSCRIPTION, NOT JUST AN Active ROW. The old free plan wrote UserSubscriptions
/// rows with Status = Active and no Stripe id; counting those as paying would have let every former Free
/// tenant skip the paywall.
/// </summary>
public static class AccountAccessPolicy
{
    public static AccountAccess Resolve(
        DateTimeOffset? trialEndsAt,
        SubscriptionStatus? subscriptionStatus,
        bool hasStripeSubscription,
        DateTimeOffset now)
    {
        if (hasStripeSubscription)
        {
            // PastDue keeps access: Stripe is still retrying the charge, and cutting a paying customer off at
            // the first failed card is exactly the interruption KAN-77 forbids. Stripe ends it (deleted →
            // Canceled) when the retries run out. Trialing is Stripe's own trial, also a live subscription.
            return subscriptionStatus is SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.Trialing
                ? new AccountAccess(AccountAccessState.Active, null, trialEndsAt, null)
                : new AccountAccess(AccountAccessState.Locked, AccountLockReason.SubscriptionEnded, trialEndsAt, null);
        }

        if (trialEndsAt is null)
            return new AccountAccess(AccountAccessState.Locked, AccountLockReason.NoSubscription, null, null);

        if (now < trialEndsAt.Value)
            return new AccountAccess(AccountAccessState.Trial, null, trialEndsAt, DaysRemaining(trialEndsAt.Value, now));

        return new AccountAccess(AccountAccessState.Locked, AccountLockReason.TrialEnded, trialEndsAt, 0);
    }

    /// <summary>
    /// Whole days left, rounded UP: with 30 hours to go the banner says 2, not 1 — telling someone they have
    /// less time than they do is the version that makes them feel cheated. Never 0 while still in trial.
    /// </summary>
    private static int DaysRemaining(DateTimeOffset trialEndsAt, DateTimeOffset now) =>
        Math.Max(1, (int)Math.Ceiling((trialEndsAt - now).TotalDays));
}
