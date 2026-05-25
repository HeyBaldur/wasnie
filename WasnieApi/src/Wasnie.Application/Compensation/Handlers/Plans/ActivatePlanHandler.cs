using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class ActivatePlanHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser)
    : IRequestHandler<ActivatePlanCommand, Result>
{
    public async Task<Result> Handle(ActivatePlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await db.CompensationPlans
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan is null)
        {
            return Result.Failure("Plan not found.");
        }

        try
        {
            plan.Activate(currentUser.UserId ?? "system");
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
