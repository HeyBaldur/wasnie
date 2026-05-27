using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class ClonePlanVersionHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid)
    : IRequestHandler<ClonePlanVersionCommand, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> Handle(ClonePlanVersionCommand request, CancellationToken cancellationToken)
    {
        var source = await db.CompensationPlans
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (source is null)
        {
            return Result<PlanDto>.Failure("Plan not found.");
        }

        try
        {
            var clone = source.CloneAsNewVersion(currentUser.UserId ?? "system", clock.UtcNowOffset, guid.NewGuid);
            db.CompensationPlans.Add(clone);
            await db.SaveChangesAsync(cancellationToken);
            return Result<PlanDto>.Success(CompensationMapper.ToPlanDto(clone));
        }
        catch (DomainException ex)
        {
            return Result<PlanDto>.Failure(ex.Message);
        }
    }
}
