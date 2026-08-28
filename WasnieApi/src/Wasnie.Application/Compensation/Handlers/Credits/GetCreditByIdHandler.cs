using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Credits;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
namespace Wasnie.Application.Compensation.Handlers.Credits;

public sealed class GetCreditByIdHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetCreditByIdQuery, Result<CreditDetailDto>>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<Result<CreditDetailDto>> Handle(
        GetCreditByIdQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.CreditsRead, cancellationToken);

        var credit = await db.Credits
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (credit is null)
            return Result<CreditDetailDto>.Failure("Credit not found.");

        var tx = await db.CompensationTransactions
            .FirstOrDefaultAsync(t => t.Id == credit.TransactionId, cancellationToken);

        var payee = credit.PayeeId != Guid.Empty
            ? await db.Payees
                .Where(p => p.Id == credit.PayeeId)
                .Select(p => new { p.FullName, p.EmployeeCode })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var plan = await db.CompensationPlans
            .Where(p => p.Id == credit.PlanId)
            .Select(p => new { p.Name, p.Status })
            .FirstOrDefaultAsync(cancellationToken);

        var snapshot = credit.RuleSnapshot;
        var rateTable = snapshot.RateTable;

        var trigger = snapshot.Trigger;
        string? triggerSummary = trigger.Conditions.Count > 0
            ? JsonSerializer.Serialize(trigger, JsonOpts)
            : null;

        // Serialize snapshot as readable JSON for Section D fallback.
        var snapshotJson = JsonSerializer.Serialize(new
        {
            ruleName = snapshot.RuleName,
            rateTable = rateTable,
            trigger = trigger,
            frozenAt = snapshot.FrozenAt
        }, JsonOpts);

        var dto = new CreditDetailDto(
            // A
            Id: credit.Id,
            IsSuperseded: credit.SupersededAt.HasValue,
            SupersededAt: credit.SupersededAt,
            SupersededBy: credit.SupersededBy,
            AllocatedAt: credit.AllocatedAt,
            AllocatedBy: credit.AllocatedBy,
            OriginalAmount: credit.OriginalAmount.Amount,
            OriginalCurrency: credit.OriginalAmount.Currency,
            CreditedAmount: credit.CreditedAmount.Amount,
            CreditedCurrency: credit.CreditedAmount.Currency,
            SplitPercentage: credit.SplitPercentage.Value * 100,
            Role: credit.Role.ToString(),
            // B
            TransactionId: credit.TransactionId,
            ReferenceNumber: tx?.ReferenceNumber ?? credit.TransactionId.ToString("N"),
            TransactionDate: tx?.TransactionDate ?? DateOnly.MinValue,
            TransactionAmount: tx?.Amount.Amount ?? 0,
            TransactionCurrency: tx?.Amount.Currency ?? credit.OriginalAmount.Currency,
            TransactionQuantity: tx?.Quantity ?? 1,
            TransactionStatus: tx?.Status.ToString() ?? "Unknown",
            TransactionPayeeId: tx?.PayeeId,
            PayeeName: payee?.FullName,
            PayeeCode: payee?.EmployeeCode,
            // C
            PlanId: credit.PlanId,
            PlanName: plan?.Name ?? credit.PlanId.ToString("N")[..8],
            PlanVersion: snapshot.PlanVersion,
            PlanStatus: plan?.Status.ToString() ?? "Unknown",
            RuleId: credit.RuleId,
            RuleName: snapshot.RuleName,
            // D
            RateTableType: rateTable.Type.ToString(),
            FlatRate: rateTable.FlatRate,
            // ★ FROM THE SNAPSHOT, so the credit describes the rule as it was when it was earned.
            MeasurementBase: Wasnie.Application.Assistant.Tools.PlanRuleSemantics
                .BaseOf(snapshot.Measurement.Type).ToString(),
            RuleSnapshotJson: snapshotJson,
            TriggerAlways: trigger.Conditions.Count == 0,
            TriggerSummary: triggerSummary);

        return Result<CreditDetailDto>.Success(dto);
    }
}
