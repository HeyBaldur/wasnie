using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Models;

namespace Wasnie.Infrastructure.Persistence.Extensions;

public static class QueryableExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<T> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }
}
