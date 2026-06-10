using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Wasnie.Application.BackgroundJobs;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Application.Models.Calculation;

namespace Wasnie.Infrastructure.BackgroundJobs;

public sealed class CalculatePayoutsJobHandler(
    IMediator mediator,
    ILogger<CalculatePayoutsJobHandler> logger)
    : JobHandlerBase<CalculatePayoutsPayload>
{
    public override async Task HandleAsync(
        CalculatePayoutsPayload payload,
        JobContext context,
        CancellationToken ct)
    {
        logger.LogInformation(
            "CalculatePayoutsJob {JobId}: starting. Period={Start}–{End}, Tenant={TenantId}, " +
            "PayeeFilter={PayeeFilter}, TriggeredBy={TriggeredBy}",
            context.JobId, payload.PeriodStart, payload.PeriodEnd,
            payload.TenantId, payload.PayeeIdFilter, payload.TriggeredBy);

        var command = new CalculatePayoutsForPeriodCommand(
            payload.PeriodStart,
            payload.PeriodEnd,
            payload.PayeeIdFilter);

        var result = await mediator.Send(command, ct);

        if (!result.IsSuccess)
        {
            logger.LogError(
                "CalculatePayoutsJob {JobId}: command failed — {Error}", context.JobId, result.Error);
            throw new InvalidOperationException(result.Error);
        }

        var summary = result.Value!;

        logger.LogInformation(
            "CalculatePayoutsJob {JobId}: complete. PayoutsCreated={Created}, Conflicts={Conflicts}, " +
            "Warnings={Warnings}",
            context.JobId, summary.PayoutsCreated, summary.Conflicts.Count, summary.Warnings.Count);

        if (summary.Warnings.Count > 0)
        {
            foreach (var w in summary.Warnings)
            {
                logger.LogWarning(
                    "CalculatePayoutsJob {JobId}: WARNING — payee={PayeeId} ({PayeeName}), " +
                    "plan={PlanId}, period={Start}–{End}: {Count} Pending transactions without Credits. " +
                    "Run ProcessPendingTransactions first to avoid underpayment.",
                    context.JobId, w.PayeeId, w.PayeeName, w.PlanId,
                    w.PeriodStart, w.PeriodEnd, w.PendingTransactionCount);
            }
        }

        await context.SetResultSummaryAsync(JsonSerializer.Serialize(new
        {
            summary.PayoutsCreated,
            Conflicts = summary.Conflicts.Select(c => new
            {
                c.PayeeId, c.PayeeName, c.PlanId,
                PeriodStart = c.PeriodStart.ToString("yyyy-MM-dd"),
                PeriodEnd = c.PeriodEnd.ToString("yyyy-MM-dd"),
                c.Status
            }),
            Warnings = summary.Warnings.Select(w => new
            {
                w.PayeeId, w.PayeeName, w.PlanId,
                PeriodStart = w.PeriodStart.ToString("yyyy-MM-dd"),
                PeriodEnd = w.PeriodEnd.ToString("yyyy-MM-dd"),
                w.PendingTransactionCount
            })
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), ct);
    }
}
