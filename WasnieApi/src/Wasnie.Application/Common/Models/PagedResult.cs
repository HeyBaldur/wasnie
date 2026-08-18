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

    /// <summary>
    /// A page with nothing on it, echoing the caller's paging parameters.
    ///
    /// Used by the payee-scoped list endpoints when the caller may not see the payee: an empty page is
    /// already what an unknown payee produces, so answering with it keeps "not yours" and "no such
    /// payee" indistinguishable (see PayeeAccessDenied for why that matters).
    /// </summary>
    public static PagedResult<T> Empty(int page, int pageSize) => new()
    {
        Items = [],
        TotalCount = 0,
        Page = page,
        PageSize = pageSize,
    };
}
