using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.PayRuns;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.PayRuns;

public sealed class GetPayRunOverlapsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetPayRunOverlapsQuery, Result<IReadOnlyList<OverlappingPayRunDto>>>
{
    public async Task<Result<IReadOnlyList<OverlappingPayRunDto>>> Handle(
        GetPayRunOverlapsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsRead, cancellationToken);

        var payRun = await db.PayRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.PayRunId, cancellationToken);

        if (payRun is null)
            return Result<IReadOnlyList<OverlappingPayRunDto>>.Failure("Pay run not found.");

        // Two periods [a1,a2] and [b1,b2] overlap iff a1 <= b2 AND b1 <= a2 (inclusive endpoints).
        var overlapping = await db.PayRuns
            .AsNoTracking()
            .Where(r => r.Id != request.PayRunId
                     && (r.Status == PayRunStatus.Approved || r.Status == PayRunStatus.Paid)
                     && r.PeriodStart <= payRun.PeriodEnd
                     && r.PeriodEnd >= payRun.PeriodStart)
            .OrderByDescending(r => r.PeriodStart)
            .ToListAsync(cancellationToken);

        var dtos = overlapping
            .Select(r => new OverlappingPayRunDto(
                r.Id, r.PeriodStart, r.PeriodEnd, r.Status.ToString(),
                r.PayeeCount, r.TotalAmounts, r.ApprovedAt, r.PaidAt))
            .ToList();

        return Result<IReadOnlyList<OverlappingPayRunDto>>.Success(dtos);
    }
}
