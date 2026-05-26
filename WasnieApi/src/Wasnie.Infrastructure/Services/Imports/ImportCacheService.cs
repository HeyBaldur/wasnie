using Microsoft.Extensions.Caching.Memory;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;

namespace Wasnie.Infrastructure.Services.Imports;

public sealed class ImportCacheService(IMemoryCache cache) : IImportCacheService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    public string Store(ParsedFile file, string originalFileName)
    {
        var fileId = Guid.NewGuid().ToString("N");
        cache.Set(CacheKey(fileId), (file, originalFileName), Ttl);
        return fileId;
    }

    public (ParsedFile File, string OriginalFileName)? Retrieve(string fileId)
    {
        return cache.TryGetValue(CacheKey(fileId), out (ParsedFile File, string OriginalFileName) entry)
            ? entry
            : null;
    }

    public void Remove(string fileId) => cache.Remove(CacheKey(fileId));

    private static string CacheKey(string fileId) => $"import:payees:{fileId}";
}
