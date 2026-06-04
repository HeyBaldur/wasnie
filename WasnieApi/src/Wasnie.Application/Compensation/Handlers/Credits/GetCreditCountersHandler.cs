using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Credits;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Credits;

public sealed class GetCreditCountersHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetCreditCountersQuery, Result<CreditCountersDto>>
{
    public async Task<Result<CreditCountersDto>> Handle(
        GetCreditCountersQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.CreditsRead, cancellationToken);

        // Start from full tenant scope (no status filter) for active/superseded counts.
        var baseQuery = ListCreditsHandler.BuildQuery(db, request.Filter with { Status = "All" });

        var activeCount = await baseQuery.CountAsync(c => c.SupersededAt == null, cancellationToken);
        var supersededCount = await baseQuery.CountAsync(c => c.SupersededAt != null, cancellationToken);

        // Active totals per currency.
        var activeCredits = await baseQuery
            .Where(c => c.SupersededAt == null)
            .Select(c => new { c.CreditedAmount.Amount, c.CreditedAmount.Currency })
            .ToListAsync(cancellationToken);

        var totals = activeCredits
            .GroupBy(c => c.Currency)
            .Select(g => new CurrencyTotalDto(g.Sum(c => c.Amount), g.Key))
            .OrderBy(t => t.Currency)
            .ToList();

        return Result<CreditCountersDto>.Success(
            new CreditCountersDto(activeCount, supersededCount, totals));
    }
}
