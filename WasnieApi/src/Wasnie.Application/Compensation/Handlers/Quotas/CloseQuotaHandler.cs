using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Quotas;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class CloseQuotaHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser)
    : IRequestHandler<CloseQuotaCommand, Result>
{
    public async Task<Result> Handle(CloseQuotaCommand request, CancellationToken cancellationToken)
    {
        var quota = await db.Quotas
            .FirstOrDefaultAsync(q => q.Id == request.QuotaId, cancellationToken);

        if (quota is null)
        {
            return Result.Failure("Quota not found.");
        }

        try
        {
            quota.Close(currentUser.UserId ?? "system");
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
