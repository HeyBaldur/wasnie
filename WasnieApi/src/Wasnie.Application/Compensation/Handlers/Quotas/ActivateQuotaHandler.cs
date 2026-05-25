using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Quotas;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class ActivateQuotaHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser)
    : IRequestHandler<ActivateQuotaCommand, Result>
{
    public async Task<Result> Handle(ActivateQuotaCommand request, CancellationToken cancellationToken)
    {
        var quota = await db.Quotas
            .FirstOrDefaultAsync(q => q.Id == request.QuotaId, cancellationToken);

        if (quota is null)
        {
            return Result.Failure("Quota not found.");
        }

        try
        {
            quota.Activate(currentUser.UserId ?? "system");
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
