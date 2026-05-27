using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Assignments;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

public sealed class DeactivateAssignmentHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid)
    : IRequestHandler<DeactivateAssignmentCommand, Result>
{
    public async Task<Result> Handle(DeactivateAssignmentCommand request, CancellationToken cancellationToken)
    {
        var assignment = await db.PlanAssignments
            .FirstOrDefaultAsync(a => a.Id == request.AssignmentId, cancellationToken);

        if (assignment is null)
        {
            return Result.Failure("Assignment not found.");
        }

        try
        {
            assignment.Deactivate(currentUser.UserId ?? "system", clock.UtcNowOffset, guid.NewGuid());
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
