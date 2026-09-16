using Wasnie.Domain.Authorization;
using Wasnie.Domain.Exceptions;
using Wasnie.Domain.Common;

namespace Wasnie.Domain.Entities;

public sealed class Tenant : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    /// <summary>
    /// LEGACY (pre-KAN-77). The old tier enum, kept in the database untouched (§B6) but no longer read or written
    /// by any access, limit or billing decision — those use <see cref="PlanCode"/> and the account access rule.
    /// </summary>
    public Tier Tier { get; private set; } = Tier.Free;

    /// <summary>
    /// The plan the tenant subscribed to (a code from Billing:Plans, e.g. "pro"). Null until they subscribe.
    /// It is the plan of their LAST subscription, not proof of paying: access comes from the subscription.
    /// </summary>
    public string? PlanCode { get; private set; }
    public bool HasSelectedPlan { get; private set; } = false;

    /// <summary>
    /// When the free trial ends (KAN-77). A FACT, set once: at registration, or by the B36 migration for tenants
    /// that existed before trials. The account state is DERIVED from it and from the subscription — see
    /// AccountAccessPolicy — so there is no lock flag here to fall out of sync.
    /// Null for tenants that never had a trial (those already paying when trials were introduced).
    /// </summary>
    public DateTimeOffset? TrialEndsAt { get; private set; }

    // Onboarding qualification — filled once during onboarding, then read-only.
    public bool IsQualified { get; private set; } = false;
    public DateTimeOffset? QualifiedAt { get; private set; }
    public string? Country { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? HowHeardAboutUs { get; private set; }
    public string? SalesVolumeRange { get; private set; }
    public string? CurrentSystem { get; private set; }
    public DateTimeOffset? LegalAcceptedAt { get; private set; }
    public string? LegalAcceptedVersion { get; private set; }

    private Tenant() { }

    public static Tenant Create(string name, string slug, Guid id, DateTimeOffset now)
    {
        return new Tenant
        {
            Id = id,
            Name = name,
            Slug = slug,
            CreatedAt = now,
            Tier = Tier.Free,
        };
    }

    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Starts the free trial. ★ ONCE: a trial that could be restarted is an unlimited free plan with extra
    /// steps, and rewriting the end date would erase when the original trial actually ended (§B6).
    /// </summary>
    public void StartTrial(DateTimeOffset endsAt)
    {
        if (TrialEndsAt is not null)
            throw new DomainException("The trial has already been started for this tenant.");

        TrialEndsAt = endsAt;
    }

    public void SetTier(Tier tier) => Tier = tier;

    /// <summary>Records the plan of the tenant's subscription (set by the Stripe webhooks).</summary>
    public void SelectPlan(string planCode)
    {
        if (string.IsNullOrWhiteSpace(planCode))
            throw new DomainException("A plan code is required.");

        PlanCode = planCode;
        HasSelectedPlan = true;
    }

    public void Qualify(
        string country,
        string phoneNumber,
        string howHeardAboutUs,
        string salesVolumeRange,
        string currentSystem,
        DateTimeOffset legalAcceptedAt,
        string legalAcceptedVersion,
        DateTimeOffset now)
    {
        Country = country;
        PhoneNumber = phoneNumber;
        HowHeardAboutUs = howHeardAboutUs;
        SalesVolumeRange = salesVolumeRange;
        CurrentSystem = currentSystem;
        LegalAcceptedAt = legalAcceptedAt;
        LegalAcceptedVersion = legalAcceptedVersion;
        IsQualified = true;
        QualifiedAt = now;
    }
}
