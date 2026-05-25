using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Quotas;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class GetQuotaByIdHandler(IApplicationDbContext db)
    : IRequestHandler<GetQuotaByIdQuery, Result<QuotaDto>>
{
    public async Task<Result<QuotaDto>> Handle(GetQuotaByIdQuery request, CancellationToken cancellationToken)
    {
        var quota = await db.Quotas
            .FirstOrDefaultAsync(q => q.Id == request.QuotaId, cancellationToken);

        return quota is null
            ? Result<QuotaDto>.Failure("Quota not found.")
            : Result<QuotaDto>.Success(CompensationMapper.ToQuotaDto(quota));
    }
}
