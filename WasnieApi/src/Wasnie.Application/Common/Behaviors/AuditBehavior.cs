using MediatR;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Application.Common.Behaviors;

public sealed class AuditBehavior<TRequest, TResponse>(
    IAuditDispatcher dispatcher,
    ITenantContext tenantContext,
    ICurrentUserService currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IAuditableCommand auditCmd)
            return await next();

        var response = await next();

        var entry = new AuditEntry(
            TenantId: tenantContext.TenantId,
            Action: auditCmd.AuditAction,
            ResourceType: auditCmd.AuditResourceType,
            ResourceId: auditCmd.AuditResourceId ?? string.Empty,
            ActorUserId: currentUser.UserId ?? "system",
            ActorEmail: currentUser.Email ?? string.Empty,
            DisplayName: auditCmd.AuditDisplayName);

        try
        {
            await dispatcher.DispatchAsync(entry, cancellationToken);
        }
        catch
        {
            // Audit failures must not block the user operation (per Rule 5.3.3)
        }

        return response;
    }
}
