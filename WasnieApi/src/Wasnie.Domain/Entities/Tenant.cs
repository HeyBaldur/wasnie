using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common;

namespace Wasnie.Domain.Entities;

public sealed class Tenant : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public Tier Tier { get; private set; } = Tier.Free;
    public bool HasSelectedPlan { get; private set; } = false;

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

    public void SetTier(Tier tier) => Tier = tier;

    public void SelectPlan(Tier tier)
    {
        Tier = tier;
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
