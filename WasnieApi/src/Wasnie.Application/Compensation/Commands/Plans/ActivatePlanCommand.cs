using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Plans;

public sealed record ActivatePlanCommand(Guid PlanId)
    : IRequest<Result>, IAuditableCommand
{
    public string AuditAction => AuditActions.PlanActivated;
    public string AuditResourceType => ResourceTypes.Plan;
    public string? AuditResourceId => PlanId.ToString();
    public string? AuditDisplayName => null;
}
