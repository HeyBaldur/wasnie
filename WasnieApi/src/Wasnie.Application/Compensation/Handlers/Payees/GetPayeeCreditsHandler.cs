using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Credits;
using Wasnie.Application.Compensation.Queries.Credits;
using Wasnie.Application.Compensation.Queries.Payees;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Payees;

public sealed class GetPayeeCreditsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IClock clock)
    : IRequestHandler<GetPayeeCreditsQuery, Result<PagedResult<CreditListDto>>>
{
    public async Task<Result<PagedResult<CreditListDto>>> Handle(
        GetPayeeCreditsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.CreditsRead, cancellationToken);

        var payeeId = request.PayeeId;
        var isActive = !string.Equals(request.Period, "all", StringComparison.OrdinalIgnoreCase);

        var query = db.Credits
            .Where(c => c.PayeeId == payeeId && c.SupersededAt == null);

        // Period filter: "active" = allocated within last 90 days
        if (isActive)
        {
            var cutoff = clock.UtcNowOffset.AddDays(-90);
            query = query.Where(c => c.AllocatedAt >= cutoff);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var credits = await query
            .OrderByDescending(c => c.AllocatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = await ListCreditsHandler.EnrichPageAsync(db, credits, new CreditFilterQuery(), cancellationToken);

        return Result<PagedResult<CreditListDto>>.Success(new PagedResult<CreditListDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
        });
    }
}
