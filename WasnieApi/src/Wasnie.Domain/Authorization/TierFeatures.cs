namespace Wasnie.Domain.Authorization;

/// <summary>
/// ★ THE ONE PLACE that answers "does this tier include the features we do not give away?".
///
/// <see cref="TierLimits"/> answers "how MANY" (payees, plans) — a quantity every tier has, only
/// bigger as you pay more. This answers "whether at all", which is a different question and belongs
/// in a different type: some capabilities are not a bigger number on Free, they are absent.
///
/// TODAY the two capabilities behind this line are the AI assistant and the HubSpot integration, and
/// they are here for the same reason: both spend real money per use that a free tenant does not pay
/// for. The assistant runs a large model on every turn; the CRM sync is continuous outbound API
/// traffic, webhook handling and id-mapping storage that keeps running whether or not anyone logs in.
/// A quota would not help — the cost is in the process existing at all.
///
/// The rule is deliberately expressed as "not Free" rather than a list of paid tiers, so a tier added
/// tomorrow (or Enterprise, added yesterday) is included by default. The failure mode of the
/// allow-list version is silent: a new paid tier that nobody remembers to add here would be sold as
/// including the assistant and then denied it at runtime.
/// </summary>
public static class TierFeatures
{
    /// <summary>
    /// True when the tenant's plan includes the metered capabilities (AI assistant, CRM integrations).
    /// Free is the only tier that does not.
    /// </summary>
    public static bool IncludesPaidFeatures(Tier tier) => tier != Tier.Free;

    /// <summary>The cheapest tier that unlocks them — what the UI offers as the upgrade target.</summary>
    public static Tier MinimumPaidTier => Tier.Starter;
}
