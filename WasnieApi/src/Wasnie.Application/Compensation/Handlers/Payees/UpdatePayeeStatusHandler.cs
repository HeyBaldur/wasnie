using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payees;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Payees;

public sealed class MarkPayeeAsActiveHandler(IApplicationDbContext db, ICurrentUserService currentUser, IClock clock)
    : IRequestHandler<MarkPayeeAsActiveCommand, Result>
{
    public async Task<Result> Handle(MarkPayeeAsActiveCommand request, CancellationToken cancellationToken)
    {
        var payee = await db.Payees.FirstOrDefaultAsync(p => p.Id == request.PayeeId, cancellationToken);
        if (payee is null) return Result.Failure("Payee not found.");
        payee.MarkAsActive(currentUser.UserId ?? "system", clock.UtcNowOffset);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class MarkPayeeAsOnLeaveHandler(IApplicationDbContext db, ICurrentUserService currentUser, IClock clock)
    : IRequestHandler<MarkPayeeAsOnLeaveCommand, Result>
{
    public async Task<Result> Handle(MarkPayeeAsOnLeaveCommand request, CancellationToken cancellationToken)
    {
        var payee = await db.Payees.FirstOrDefaultAsync(p => p.Id == request.PayeeId, cancellationToken);
        if (payee is null) return Result.Failure("Payee not found.");
        try { payee.MarkAsOnLeave(currentUser.UserId ?? "system", clock.UtcNowOffset); }
        catch (Exception ex) { return Result.Failure(ex.Message); }
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class MarkPayeeAsTerminatedHandler(IApplicationDbContext db, ICurrentUserService currentUser, IClock clock)
    : IRequestHandler<MarkPayeeAsTerminatedCommand, Result>
{
    public async Task<Result> Handle(MarkPayeeAsTerminatedCommand request, CancellationToken cancellationToken)
    {
        var payee = await db.Payees.FirstOrDefaultAsync(p => p.Id == request.PayeeId, cancellationToken);
        if (payee is null) return Result.Failure("Payee not found.");
        payee.MarkAsTerminated(request.TerminationDate, currentUser.UserId ?? "system", clock.UtcNowOffset);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
