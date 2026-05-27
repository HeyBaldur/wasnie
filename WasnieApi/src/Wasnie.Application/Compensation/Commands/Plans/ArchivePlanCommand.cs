using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Plans;

public sealed record ArchivePlanCommand(Guid PlanId)
    : IRequest<Result>, IAuditableCommand
{
    public string AuditAction => AuditActions.PlanArchived;
    public string AuditResourceType => ResourceTypes.Plan;
    public string? AuditResourceId => PlanId.ToString();
    public string? AuditDisplayName => null;
}
