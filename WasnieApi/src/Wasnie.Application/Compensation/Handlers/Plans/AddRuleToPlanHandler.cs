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

public sealed class AddRuleToPlanHandler(IApplicationDbContext db, IAuthorizationService authorizationService)
    : IRequestHandler<AddRuleToPlanCommand, Result<RuleDto>>
{
    public async Task<Result<RuleDto>> Handle(AddRuleToPlanCommand request, CancellationToken cancellationToken)
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
            var rule = plan.AddRule(
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
        // ★ THE CODED ONES GO PAST, AND THAT IS THE WHOLE POINT. Result carries a single string, so
        // anything caught here loses its code and its parameters and arrives at the browser as an
        // English sentence. Rethrowing lets ExceptionHandlingMiddleware emit the 422 with the code
        // intact, which is what makes the rate-table refusals translatable.
        //
        // Nothing has been persisted at this point: the only coded throw on this path is
        // RateTableRequest.ToDomain, evaluated as an argument — before AddRule mutates anything and
        // long before SaveChangesAsync.
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
