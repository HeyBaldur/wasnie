using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payees;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Payees;

public sealed class DeactivatePayeeHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IAuthorizationService authorizationService)
    : IRequestHandler<DeactivatePayeeCommand, Result>
{
    public async Task<Result> Handle(DeactivatePayeeCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayeesDeactivate, cancellationToken);

        var payee = await db.Payees.FirstOrDefaultAsync(p => p.Id == request.PayeeId, cancellationToken);
        if (payee is null) return Result.Failure("Payee not found.");

        payee.Deactivate(currentUser.UserId ?? "system", clock.UtcNowOffset);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class ActivatePayeeHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IAuthorizationService authorizationService)
    : IRequestHandler<ActivatePayeeCommand, Result>
{
    public async Task<Result> Handle(ActivatePayeeCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayeesDeactivate, cancellationToken);

        var payee = await db.Payees.FirstOrDefaultAsync(p => p.Id == request.PayeeId, cancellationToken);
        if (payee is null) return Result.Failure("Payee not found.");

        payee.Activate(currentUser.UserId ?? "system", clock.UtcNowOffset);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
