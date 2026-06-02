using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Payees;

public sealed record DeactivatePayeeCommand(Guid PayeeId) : IRequest<Result>, IAuditableCommand
{
    public string AuditAction => AuditActions.PayeeDeactivated;
    public string AuditResourceType => ResourceTypes.Payee;
    public string? AuditResourceId => PayeeId.ToString();
    public string? AuditDisplayName => null;
}

public sealed record ActivatePayeeCommand(Guid PayeeId) : IRequest<Result>, IAuditableCommand
{
    public string AuditAction => AuditActions.PayeeActivated;
    public string AuditResourceType => ResourceTypes.Payee;
    public string? AuditResourceId => PayeeId.ToString();
    public string? AuditDisplayName => null;
}
