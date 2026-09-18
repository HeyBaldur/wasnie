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
        // ★★ `Plans.ReadOwn` OPENS THIS TOO, AND THE RULE IS UNREADABLE WITHOUT IT. This is the
        // catalogue of transaction FIELDS a trigger can test — "product", "category", "amount". The
        // rule screen renders a condition's field through it, so a reader who cannot load it sees a
        // condition with a BLANK field name: a screen quietly lying about how somebody is paid, which
        // is worse than the Access Denied it replaced (§C3).
        //
        // ★ IT DISCLOSES NOTHING. These are the engine's own field names, identical for every tenant
        // — a schema description, not anybody's data. That is what makes widening it safe, and why
        // `GetCategoryValues` next door is NOT widened: those are the tenant's real category values.
        if (!await authorizationService.HasAsync(Permission.PlansReadOwn, cancellationToken))
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
