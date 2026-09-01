using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class UpdateRuleHandler(IApplicationDbContext db, IAuthorizationService authorizationService)
    : IRequestHandler<UpdateRuleCommand, Result<RuleDto>>
{
    public async Task<Result<RuleDto>> Handle(UpdateRuleCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PlansUpdate, cancellationToken);
        var plan = await db.CompensationPlans
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan is null)
        {
            return Result<RuleDto>.Failure("Plan not found.");
        }

        // Fail loud: the engine only honors PerTransaction caps today (PerPeriod/Total are
        // deferred). Reject other scopes instead of storing a cap the engine silently ignores.
        if (request.Cap is not null && request.Cap.Scope != CapScope.PerTransaction)
        {
            return Result<RuleDto>.Failure("Only Per Transaction cap scope is currently supported.");
        }

        try
        {
            // ★ THE RETURNED RULE, NOT A LOOKUP BY request.RuleId. Editing a STOPPED rule supersedes
            // it rather than reviving it, so what comes back carries a NEW Id — and re-finding the
            // requested id here would answer with the stopped predecessor, telling the screen the
            // save did nothing.
            var rule = plan.UpdateRule(
                request.RuleId,
                request.Name,
                request.SortOrder,
                request.Measurement,
                request.RateTable.ToDomain(),
                request.Trigger,
                request.Modifier,
                request.Cap,
                request.Floor);

            await db.SaveChangesAsync(cancellationToken);

            return Result<RuleDto>.Success(CompensationMapper.ToRuleDto(rule));
        }
        // ★ See AddRuleToPlanHandler: Result carries one string, so a coded refusal caught here would
        // arrive at the browser as an English sentence with its code and parameters stripped.
        // Rethrowing is what keeps the rate-table refusals translatable. Nothing is persisted yet —
        // ToDomain is evaluated as an argument, before UpdateRule and before SaveChangesAsync.
        catch (DomainCodedException)
        {
            throw;
        }
        catch (DomainException ex)
        {
            return Result<RuleDto>.Failure(ex.Message);
        }
    }
}
