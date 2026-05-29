using Microsoft.Extensions.Caching.Memory;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;

namespace Wasnie.Infrastructure.Services.Imports;

public sealed class ImportCacheService(IMemoryCache cache, ITenantContext tenantContext) : IImportCacheService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    public string Store(ParsedFile file, string originalFileName, string resource = "payees")
    {
        var fileId = Guid.NewGuid().ToString("N");
        cache.Set(CacheKey(fileId, resource), (file, originalFileName), Ttl);
        return fileId;
    }

    public (ParsedFile File, string OriginalFileName)? Retrieve(string fileId, string resource = "payees")
    {
        return cache.TryGetValue(CacheKey(fileId, resource), out (ParsedFile File, string OriginalFileName) entry)
            ? entry
            : null;
    }

    public void Remove(string fileId, string resource = "payees") => cache.Remove(CacheKey(fileId, resource));

    private string CacheKey(string fileId, string resource)
    {
        var tenantId = tenantContext.TenantId;
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException("Cannot access import cache without a tenant context.");
        return $"import:{resource}:{tenantId}:{fileId}";
    }
}
