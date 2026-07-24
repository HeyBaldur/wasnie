using MediatR;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Enrichment;

public sealed record ListCategoryMappingsQuery(PaginationQuery Pagination)
    : IRequest<Result<PagedResult<CategoryMappingDto>>>;
