using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Helpers;
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
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var (from, to) = PeriodHelper.ComputeDateRange(request.Period, today);

        var query = db.Credits
            .Where(c => c.PayeeId == payeeId && c.SupersededAt == null);

        // Period filter: scope credits by the underlying transaction's TransactionDate.
        // This is consistent with how quotas and assignments are scoped.
        if (from.HasValue || to.HasValue)
        {
            var validTxIds = db.CompensationTransactions
                .Where(t =>
                    (!from.HasValue || t.TransactionDate >= from.Value) &&
                    (!to.HasValue || t.TransactionDate <= to.Value))
                .Select(t => t.Id);
            query = query.Where(c => validTxIds.Contains(c.TransactionId));
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
