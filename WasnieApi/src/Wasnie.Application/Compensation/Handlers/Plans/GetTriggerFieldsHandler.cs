using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class GetTriggerFieldsHandler(IAuthorizationService authorizationService)
    : IRequestHandler<GetTriggerFieldsQuery, Result<IReadOnlyList<TriggerFieldDto>>>
{
    public async Task<Result<IReadOnlyList<TriggerFieldDto>>> Handle(
        GetTriggerFieldsQuery request, CancellationToken cancellationToken)
    {
        // Reading this is part of authoring a plan's rules.
        await authorizationService.RequireAsync(Permission.PlansRead, cancellationToken);

        // Projected straight off the catalog — no second list to keep in sync.
        var fields = TriggerFieldCatalog.Fields
            .Select(f => new TriggerFieldDto(
                Field: f.Field,
                ValueType: f.ValueType.ToString(),
                Operators: f.Operators
                    .Select(op => new TriggerOperatorDto(op.ToString(), TriggerFieldCatalog.UsesSet(op)))
                    .ToList()))
            .ToList();

        return Result<IReadOnlyList<TriggerFieldDto>>.Success(fields);
    }
}
