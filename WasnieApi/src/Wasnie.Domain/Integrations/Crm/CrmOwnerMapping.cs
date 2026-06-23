using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Integrations.Crm;

/// <summary>
/// Links a CRM owner (e.g. a HubSpot owner id) to a Wasnie payee, so deals owned by that CRM user are
/// attributed to the right payee. Generalised to multi-CRM via <see cref="Source"/> ("HubSpot", ...).
///
/// CRITICAL (financial software): a mapping decides WHO gets paid for a deal. A wrong mapping pays the
/// wrong person — so the link is explicit and unique per (tenant, source, owner). It is created either by
/// an exact email auto-match (<see cref="CrmOwnerMatchMethod.Email"/>) or by a user in the manual mapping
/// queue (<see cref="CrmOwnerMatchMethod.Manual"/>).
///
/// Tenant-scoped (Rule 9). One owner maps to at most one payee per source.
/// </summary>
public sealed class CrmOwnerMapping : Entity
{
    public Guid TenantId { get; private set; }

    /// <summary>The CRM this owner belongs to (e.g. "HubSpot").</summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>The CRM's stable owner identifier (e.g. HubSpot's hubspot_owner_id / owner id).</summary>
    public string CrmOwnerId { get; private set; } = string.Empty;

    /// <summary>The Wasnie payee this owner resolves to.</summary>
    public Guid PayeeId { get; private set; }

    public CrmOwnerMatchMethod MatchMethod { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private CrmOwnerMapping() { }

    public static CrmOwnerMapping Create(
        Guid id,
        Guid tenantId,
        string source,
        string crmOwnerId,
        Guid payeeId,
        CrmOwnerMatchMethod matchMethod,
        string createdBy,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");
        if (string.IsNullOrWhiteSpace(source))
            throw new DomainException("CRM source is required.");
        if (string.IsNullOrWhiteSpace(crmOwnerId))
            throw new DomainException("CRM owner id is required.");
        if (payeeId == Guid.Empty)
            throw new DomainException("PayeeId must not be empty.");
        if (string.IsNullOrWhiteSpace(createdBy))
            throw new DomainException("CreatedBy is required.");

        return new CrmOwnerMapping
        {
            Id = id,
            TenantId = tenantId,
            Source = source.Trim(),
            CrmOwnerId = crmOwnerId.Trim(),
            PayeeId = payeeId,
            MatchMethod = matchMethod,
            CreatedAt = now,
            CreatedBy = createdBy,
        };
    }
}
