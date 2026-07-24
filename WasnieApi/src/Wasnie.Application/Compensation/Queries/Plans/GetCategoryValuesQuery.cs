using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Plans;

/// <summary>
/// The distinct category values that exist for the current tenant — what a rule condition on the
/// <c>category</c> field can actually match. Feeds the rule builder's category picker so the value is
/// chosen from reality instead of typed by hand, where "Laptps" would save fine and never fire.
/// </summary>
public sealed record GetCategoryValuesQuery : IRequest<Result<IReadOnlyList<string>>>;
