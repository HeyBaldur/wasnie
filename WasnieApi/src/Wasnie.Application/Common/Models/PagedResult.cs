namespace Wasnie.Application.Common.Models;

public sealed class PagedResult<T>
{
    public required List<T> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    /// <summary>
    /// Tenant-wide unfiltered total, populated only by list endpoints that support
    /// advanced filtering (e.g. Transactions). Null for all other endpoints.
    /// </summary>
    public int? UnfilteredTotal { get; init; }
}
