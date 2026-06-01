using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Settings.Queries;

public sealed record FieldRequirementDto(string EntityName, string FieldName, bool IsRequired);

public sealed record GetFieldRequirementsQuery : IRequest<Result<List<FieldRequirementDto>>>;

public sealed class GetFieldRequirementsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetFieldRequirementsQuery, Result<List<FieldRequirementDto>>>
{
    public async Task<Result<List<FieldRequirementDto>>> Handle(
        GetFieldRequirementsQuery request,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.SettingsUpdate, cancellationToken);

        var settings = await db.FieldRequirementSettings
            .OrderBy(s => s.EntityName).ThenBy(s => s.FieldName)
            .Select(s => new FieldRequirementDto(s.EntityName, s.FieldName, s.IsRequired))
            .ToListAsync(cancellationToken);

        return Result<List<FieldRequirementDto>>.Success(settings);
    }
}
