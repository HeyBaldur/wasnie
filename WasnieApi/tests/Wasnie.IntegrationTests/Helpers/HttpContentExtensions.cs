using System.Net.Http.Json;

namespace Wasnie.IntegrationTests.Helpers;

public static class HttpContentExtensions
{
    public static async Task<List<T>> ReadPagedItemsAsync<T>(
        this HttpContent content,
        CancellationToken cancellationToken = default)
    {
        var paged = await content.ReadFromJsonAsync<PagedResult<T>>(cancellationToken);
        return paged?.Items ?? [];
    }

    public static async Task<PagedResult<T>> ReadPagedResultAsync<T>(
        this HttpContent content,
        CancellationToken cancellationToken = default)
    {
        var paged = await content.ReadFromJsonAsync<PagedResult<T>>(cancellationToken);
        return paged ?? throw new InvalidOperationException("Paged response was null.");
    }
}
