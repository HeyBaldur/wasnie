using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Settings;

namespace Wasnie.Infrastructure.Services;

public sealed class FieldRequirementService(IApplicationDbContext db) : IFieldRequirementService
{
    private List<FieldRequirementSetting>? _cache;

    public async Task<bool> IsRequiredAsync(
        string entityName,
        string fieldName,
        CancellationToken cancellationToken = default)
    {
        // Lazy-load all settings for this tenant once per request (DbContext is scoped per request;
        // global query filter ensures tenant isolation automatically).
        _cache ??= await db.FieldRequirementSettings.ToListAsync(cancellationToken);

        return _cache.FirstOrDefault(s =>
            string.Equals(s.EntityName, entityName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(s.FieldName, fieldName, StringComparison.OrdinalIgnoreCase))
            ?.IsRequired ?? false;
    }
}
