using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class AddRuleToPlanHandler(IApplicationDbContext db)
    : IRequestHandler<AddRuleToPlanCommand, Result<RuleDto>>
{
    public async Task<Result<RuleDto>> Handle(AddRuleToPlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await db.CompensationPlans
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan is null)
        {
            return Result<RuleDto>.Failure("Plan not found.");
        }

        try
        {
            var rule = plan.AddRule(
                request.Name,
                request.SortOrder,
                request.Measurement,
                request.RateTable,
                request.Trigger,
                request.Modifier,
                request.Cap,
                request.Floor);

            await db.SaveChangesAsync(cancellationToken);
            return Result<RuleDto>.Success(CompensationMapper.ToRuleDto(rule));
        }
        catch (DomainException ex)
        {
            return Result<RuleDto>.Failure(ex.Message);
        }
    }
}
