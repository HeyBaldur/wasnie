using MediatR;
using Wasnie.Application.Common.Extensions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Enrichment;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Enrichment;

public sealed class ListCategoryMappingsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<ListCategoryMappingsQuery, Result<PagedResult<CategoryMappingDto>>>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase) { "inputfield", "inputvalue", "category" };

    public async Task<Result<PagedResult<CategoryMappingDto>>> Handle(
        ListCategoryMappingsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.CategoryMappingsRead, cancellationToken);
        var p = request.Pagination;
        var query = db.CategoryMappings.AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.Search))
        {
            var q = p.Search.Trim().ToLower();
            query = query.Where(m =>
                m.InputValue.ToLower().Contains(q) ||
                m.Category.ToLower().Contains(q));
        }

        var sortBy = AllowedSortFields.Contains(p.SortBy ?? "") ? p.SortBy!.ToLower() : "category";
        var desc = string.Equals(p.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        query = sortBy switch
        {
            "inputfield" => desc ? query.OrderByDescending(m => m.InputField) : query.OrderBy(m => m.InputField),
            "inputvalue" => desc ? query.OrderByDescending(m => m.InputValue) : query.OrderBy(m => m.InputValue),
            _ => desc ? query.OrderByDescending(m => m.Category) : query.OrderBy(m => m.Category),
        };

        var paged = await query.ToPagedResultAsync(p.Page, p.PageSize, cancellationToken);

        return Result<PagedResult<CategoryMappingDto>>.Success(new PagedResult<CategoryMappingDto>
        {
            Items = paged.Items.Select(CreateCategoryMappingHandler.ToDto).ToList(),
            TotalCount = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
        });
    }
}
