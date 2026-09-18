using MediatR;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Plans;

/// <summary>
/// The catalogue of transaction FIELDS a trigger can test — "product", "category", "amount".
///
/// ★★ IT ASKS FOR NO PERMISSION, AND IT TOOK TWO WIDENINGS TO SEE THAT WAS THE ANSWER. It required
/// <c>Plans.Read</c>; KAN-93 added <c>Plans.ReadOwn</c> so a rep could read the rules of their own plan
/// without a BLANK field name where the condition should be — a screen quietly lying about how somebody
/// is paid, which is worse than the Access Denied it replaced (§C3). Then a Manager took the guided
/// tour, held NEITHER key, and hit exactly the same blank. Widening the same gate twice is the signal
/// that the gate was never the right question.
///
/// ★★ AND IT DISCLOSES NOTHING, which is what makes that safe. These are the ENGINE's own field names,
/// identical in every workspace — a schema description, not anybody's data. <c>[Authorize]</c> on the
/// controller still requires a session; there is simply nothing here to scope to a role.
///
/// ★ <c>GetCategoryValues</c> NEXT DOOR STAYS SHUT, and the contrast is the point: those ARE the
/// tenant's own category values. This is the catalogue; that is the data.
/// </summary>
public sealed class GetTriggerFieldsHandler
    : IRequestHandler<GetTriggerFieldsQuery, Result<IReadOnlyList<TriggerFieldDto>>>
{
    public Task<Result<IReadOnlyList<TriggerFieldDto>>> Handle(
        GetTriggerFieldsQuery request, CancellationToken cancellationToken)
    {
        // Projected straight off the catalog — no second list to keep in sync.
        var fields = TriggerFieldCatalog.Fields
            .Select(f => new TriggerFieldDto(
                Field: f.Field,
                ValueType: f.ValueType.ToString(),
                Operators: f.Operators
                    .Select(op => new TriggerOperatorDto(op.ToString(), TriggerFieldCatalog.UsesSet(op)))
                    .ToList()))
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<TriggerFieldDto>>.Success(fields));
    }
}
