using MediatR;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Application.Common.Behaviors;

public sealed class AuditBehavior<TRequest, TResponse>(
    IAuditDispatcher dispatcher,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IApplicationDbContext db)
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

        if (request is IMoneyCriticalCommand)
            return await HandleMoneyCriticalAsync(auditCmd, next, cancellationToken);

        // Non-money path (Phase 1): business first, audit with failure swallowed (Rule 5.3.3)
        var response = await next();
        var entry = BuildEntry(auditCmd);
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

    // Money-critical path: audit failure MUST roll back the business write (Rule 5.3.3).
    // BeginTransactionAsync causes SaveChangesAsync calls in next() and DispatchAsync to
    // participate in the same transaction without auto-committing. CommitAsync commits both.
    // Any exception from either side → await-using disposes tx → automatic rollback.
    private async Task<TResponse> HandleMoneyCriticalAsync(
        IAuditableCommand auditCmd,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var response = await next();
        var entry = BuildEntry(auditCmd);
        await dispatcher.DispatchAsync(entry, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return response;
    }

    private AuditEntry BuildEntry(IAuditableCommand auditCmd) =>
        new(
            TenantId: tenantContext.TenantId,
            Action: auditCmd.AuditAction,
            ResourceType: auditCmd.AuditResourceType,
            ResourceId: auditCmd.AuditResourceId ?? string.Empty,
            ActorUserId: currentUser.UserId ?? "system",
            ActorEmail: currentUser.Email ?? string.Empty,
            DisplayName: auditCmd.AuditDisplayName);
}
