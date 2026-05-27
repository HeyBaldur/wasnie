using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class DeletePlanHandler(IApplicationDbContext db, IAuthorizationService authorizationService)
    : IRequestHandler<DeletePlanCommand, Result>
{
    public async Task<Result> Handle(DeletePlanCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PlansDelete, cancellationToken);
        var plan = await db.CompensationPlans
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan is null)
            return Result.Failure("Plan not found.");

        try
        {
            plan.CheckDeletable();
            db.CompensationPlans.Remove(plan);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
