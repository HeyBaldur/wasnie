using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Quotas;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class ListQuotasByPayeeHandler(IApplicationDbContext db)
    : IRequestHandler<ListQuotasByPayeeQuery, Result<IList<QuotaDto>>>
{
    public async Task<Result<IList<QuotaDto>>> Handle(ListQuotasByPayeeQuery request, CancellationToken cancellationToken)
    {
        var quotas = await db.Quotas
            .Where(q => q.PayeeId == request.PayeeId)
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync(cancellationToken);

        return Result<IList<QuotaDto>>.Success(
            quotas.Select(CompensationMapper.ToQuotaDto).ToList());
    }
}
