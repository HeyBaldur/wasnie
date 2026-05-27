using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Assignments;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

public sealed class UpdateAssignmentNotesHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IAuthorizationService authorizationService)
    : IRequestHandler<UpdateAssignmentNotesCommand, Result>
{
    public async Task<Result> Handle(UpdateAssignmentNotesCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.AssignmentsUpdate, cancellationToken);
        var assignment = await db.PlanAssignments
            .FirstOrDefaultAsync(a => a.Id == request.AssignmentId, cancellationToken);

        if (assignment is null)
            return Result.Failure("Assignment not found.");

        assignment.UpdateNotes(request.Notes, currentUser.UserId ?? "system", clock.UtcNowOffset);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
