using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Enrichment;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Enrichment;

public sealed class DeleteCategoryMappingHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<DeleteCategoryMappingCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteCategoryMappingCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.CategoryMappingsManage, cancellationToken);

        var mapping = await db.CategoryMappings
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);
        if (mapping is null)
            return Result<bool>.Failure("Category mapping not found.");

        // Deleting a mapping only stops FUTURE ingests from resolving that category — transactions
        // already enriched keep their frozen Category (no retroactive change; WI decision #2).
        db.CategoryMappings.Remove(mapping);
        await db.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
