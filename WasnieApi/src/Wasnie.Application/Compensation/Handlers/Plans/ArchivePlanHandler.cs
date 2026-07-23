using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
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
            var actor = currentUser.UserId ?? "system";
            var now = clock.UtcNowOffset;

            plan.Archive(actor, now, guid.NewGuid());

            // Archiving a plan must also deactivate its assignments. PlanAssignment is a separate
            // aggregate (not owned by Plan), so we deactivate them here in the same transaction.
            // Otherwise the archived plan keeps being resolved/processed and credits get
            // mis-attributed to it (the resolver only checks assignment status, not plan status).
            var activeAssignments = await db.PlanAssignments
                .Where(a => a.PlanId == plan.Id && a.Status == AssignmentStatus.Active)
                .ToListAsync(cancellationToken);

            foreach (var assignment in activeAssignments)
            {
                assignment.Deactivate(actor, now, guid.NewGuid());
            }

            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
