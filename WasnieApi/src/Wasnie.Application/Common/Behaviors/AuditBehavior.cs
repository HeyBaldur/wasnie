using MediatR;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Common.Results;

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

        // KAN-34: an audit row asserts that something HAPPENED. A handler that returned
        // Result.Failure did nothing, so there is nothing to assert. See Succeeded.
        if (!Succeeded(response))
            return response;

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

        // ★ KAN-34: the money path needed this check just as much as the other one. The transaction
        // here defends against the AUDIT WRITE failing — it says nothing about the handler's Result,
        // which travels back as a return value and commits the transaction perfectly happily. Four
        // failed attempts at reverting one 2.980 EUR commission each left a row saying it was
        // reverted. The commit still runs on failure so that a handler which deliberately persisted
        // something on its way to Failure ("ingest and mark", Rule B2) keeps that write.
        if (Succeeded(response))
        {
            var entry = BuildEntry(auditCmd);
            await dispatcher.DispatchAsync(entry, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        return response;
    }

    /// <summary>
    /// Whether the handler's response reports success.
    ///
    /// ★ A response that is NOT a Result counts as a success: several auditable commands return a DTO
    /// or Unit and signal failure by throwing, and those must keep being audited exactly as before.
    /// Only an explicit <c>Result.Failure</c> suppresses the row.
    /// </summary>
    private static bool Succeeded(TResponse response) =>
        response is not IResultOutcome outcome || outcome.IsSuccess;

    private AuditEntry BuildEntry(IAuditableCommand auditCmd) =>
        new(
            TenantId: tenantContext.TenantId,
            Action: auditCmd.AuditAction,
            ResourceType: auditCmd.AuditResourceType,
            ResourceId: auditCmd.AuditResourceId ?? string.Empty,
            ActorUserId: currentUser.UserId ?? "system",
            ActorEmail: currentUser.Email ?? string.Empty,
            DisplayName: auditCmd.AuditDisplayName,
            // Null for almost every command, and that is the point: only the ones with something worth
            // recording say anything. See IAuditableCommand.AuditMetadata.
            Metadata: auditCmd.AuditMetadata);
}
