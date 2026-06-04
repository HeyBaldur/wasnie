using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Application.Models.Calculation;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Transactions;

public sealed class ProcessPendingTransactionsCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAuthorizationService authorizationService,
    IBackgroundJobService backgroundJobService)
    : IRequestHandler<ProcessPendingTransactionsCommand, Result<ProcessPendingTransactionsResult>>
{
    public async Task<Result<ProcessPendingTransactionsResult>> Handle(
        ProcessPendingTransactionsCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsProcessPending, cancellationToken);

        // Lightweight candidate count for volume awareness + UI feedback.
        var candidateCount = await GetPendingTransactionsCountHandler.CountCandidatesAsync(
            db, request.Scope, request.ScopeId, request.PeriodStart, request.PeriodEnd, cancellationToken);

        var payload = new ProcessPendingTransactionsPayload(
            TenantId: tenantContext.TenantId,
            Scope: request.Scope,
            ScopeId: request.ScopeId,
            PeriodStart: request.PeriodStart,
            PeriodEnd: request.PeriodEnd,
            TriggeredBy: currentUser.UserId ?? "system",
            TriggeredByEmail: currentUser.Email ?? string.Empty);

        var jobId = await backgroundJobService.EnqueueAsync(
            payload,
            tenantContext.TenantId,
            currentUser.UserId ?? "system",
            currentUser.Email ?? string.Empty,
            cancellationToken);

        request.AuditResourceId = jobId.ToString();

        return Result<ProcessPendingTransactionsResult>.Success(
            new ProcessPendingTransactionsResult(jobId, candidateCount));
    }
}
