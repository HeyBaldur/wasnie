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
}
