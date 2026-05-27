using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class ArchivePlanHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService)
    : IRequestHandler<ArchivePlanCommand, Result>
{
    public async Task<Result> Handle(ArchivePlanCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PlansArchive, cancellationToken);
        var plan = await db.CompensationPlans
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan is null)
        {
            return Result.Failure("Plan not found.");
        }

        try
        {
            plan.Archive(currentUser.UserId ?? "system", clock.UtcNowOffset, guid.NewGuid());
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
