using Wasnie.Domain.Integrations.Crm;

namespace Wasnie.Application.Integrations.Crm;

/// <summary>Outcome of resolving a CRM owner to a Wasnie payee.</summary>
/// <param name="PayeeId">The resolved payee, or null when the owner could not be resolved (→ Unassigned).</param>
/// <param name="NewMappingCreated">True when this call created a new <see cref="CrmOwnerMapping"/> (email auto-match).</param>
/// <param name="Method">How it resolved (null when unresolved).</param>
public sealed record CrmOwnerResolution(Guid? PayeeId, bool NewMappingCreated, CrmOwnerMatchMethod? Method)
{
    public bool IsResolved => PayeeId.HasValue;

    public static CrmOwnerResolution Unresolved() => new(null, false, null);
    public static CrmOwnerResolution FromExistingMapping(Guid payeeId, CrmOwnerMatchMethod method) =>
        new(payeeId, false, method);
    public static CrmOwnerResolution NewEmailMatch(Guid payeeId) =>
        new(payeeId, true, CrmOwnerMatchMethod.Email);
}

/// <summary>
/// Resolves a CRM owner to a Wasnie payee (FASE 2b). Strategy, in order:
/// 1. An existing <see cref="CrmOwnerMapping"/> for (tenant, source, owner) — the stable, already-resolved link.
/// 2. Otherwise an exact match on NORMALISED email (lower+trim) to a payee's email. Payee email is unique
///    per tenant (filtered index), so a match is inherently unambiguous. On a match a new mapping is
///    created (added to the context — the CALLER saves) so the link is stable from then on.
/// 3. Otherwise unresolved → the deal's transaction is created Unassigned (PayeeId null).
///
/// NEVER auto-creates a payee (a duplicate vector — explicitly rejected in the WI).
/// </summary>
public interface ICrmOwnerResolver
{
    Task<CrmOwnerResolution> ResolveAsync(
        Guid tenantId,
        string source,
        string crmOwnerId,
        string? ownerEmail,
        CancellationToken cancellationToken = default);
}
