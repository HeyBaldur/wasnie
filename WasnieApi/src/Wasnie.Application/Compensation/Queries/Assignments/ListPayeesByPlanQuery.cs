using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Assignments;

public sealed record ListPayeesByPlanQuery(Guid PlanId) : IRequest<Result<IList<PlanAssignmentDto>>>;
