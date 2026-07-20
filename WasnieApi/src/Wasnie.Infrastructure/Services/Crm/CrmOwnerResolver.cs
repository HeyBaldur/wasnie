using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Domain.Integrations.Crm;

namespace Wasnie.Infrastructure.Services.Crm;

/// <summary>
/// Default <see cref="ICrmOwnerResolver"/>: existing mapping → exact normalized-email match → unresolved.
/// Queries run under the ambient tenant query filter; a new email-match mapping is ADDED to the context
/// (the caller commits it within its own transaction so the mapping and the transaction commit atomically).
/// </summary>
public sealed class CrmOwnerResolver(
    IApplicationDbContext db,
    IClock clock,
    IGuidGenerator guid,
    ICurrentUserService currentUser)
    : ICrmOwnerResolver
{
    public async Task<CrmOwnerResolution> ResolveAsync(
        Guid tenantId,
        string source,
        string crmOwnerId,
        string? ownerEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(crmOwnerId))
            return CrmOwnerResolution.Unresolved();

        // 1. Already linked? Use the stable mapping.
        var existing = await db.CrmOwnerMappings
            .FirstOrDefaultAsync(
                m => m.Source == source && m.CrmOwnerId == crmOwnerId, cancellationToken);
        if (existing is not null)
            return CrmOwnerResolution.FromExistingMapping(existing.PayeeId, existing.MatchMethod);

        // 2. Auto-match by normalized email. Payee email is unique per tenant → at most one match.
        var normalizedEmail = NormalizeEmail(ownerEmail);
        if (normalizedEmail is null)
            return CrmOwnerResolution.Unresolved();

        var payee = await db.Payees
            .FirstOrDefaultAsync(p => p.Email == normalizedEmail, cancellationToken);
        if (payee is null)
            return CrmOwnerResolution.Unresolved();

        var mapping = CrmOwnerMapping.Create(
            id: guid.NewGuid(),
            tenantId: tenantId,
            source: source,
            crmOwnerId: crmOwnerId,
            payeeId: payee.Id,
            matchMethod: CrmOwnerMatchMethod.Email,
            createdBy: currentUser.UserId ?? "system",
            now: clock.UtcNowOffset);

        db.CrmOwnerMappings.Add(mapping);

        return CrmOwnerResolution.NewEmailMatch(payee.Id);
    }

    /// <summary>Lower-cases and trims an email so both sides compare consistently. Null/blank → null.</summary>
    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}
