using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class StopRuleHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IAuthorizationService authorizationService)
    : IRequestHandler<StopRuleCommand, Result<RuleDto>>
{
    public async Task<Result<RuleDto>> Handle(StopRuleCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PlansStopRule, cancellationToken);

        var plan = await db.CompensationPlans
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan is null)
        {
            return Result<RuleDto>.Failure("Plan not found.");
        }

        try
        {
            var rule = plan.StopRule(
                request.RuleId,
                currentUser.UserId ?? "system",
                request.Reason,
                clock.UtcNowOffset);

            await db.SaveChangesAsync(cancellationToken);
            return Result<RuleDto>.Success(CompensationMapper.ToRuleDto(rule));
        }
        // ★ Every refusal on this path is coded, and every one of them has to stay coded. Result
        // carries a single string, so catching these here would strip the code and its parameters
        // and deliver an English sentence to a Spanish or Polish screen — on the one dialog someone
        // opens because money is going out wrong. See AddRuleToPlanHandler for the same decision.
        // Nothing is persisted: the throw precedes SaveChangesAsync.
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
