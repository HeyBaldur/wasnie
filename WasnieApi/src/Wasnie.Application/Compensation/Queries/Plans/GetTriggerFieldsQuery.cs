using MediatR;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Plans;

/// <summary>
/// The transaction attributes a rule trigger can filter on, straight from
/// <see cref="TriggerFieldCatalog"/>. The rule builder consumes this instead of holding its own copy:
/// a second list in the browser is how the UI came to offer fields the engine had never heard of.
/// </summary>
public sealed record GetTriggerFieldsQuery : IRequest<Result<IReadOnlyList<TriggerFieldDto>>>;
