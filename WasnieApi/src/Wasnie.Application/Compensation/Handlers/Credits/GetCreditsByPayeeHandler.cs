using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Credits;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Credits;

public sealed class GetCreditsByPayeeHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetCreditsByPayeeQuery, Result<IReadOnlyList<CreditByPayeeDto>>>
{
    public async Task<Result<IReadOnlyList<CreditByPayeeDto>>> Handle(
        GetCreditsByPayeeQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.CreditsRead, cancellationToken);

        var query = ListCreditsHandler.BuildQuery(db, request.Filter);

        var raw = await query
            .Select(c => new
            {
                c.PayeeId,
                CreditedAmount = c.CreditedAmount.Amount,
                CreditedCurrency = c.CreditedAmount.Currency,
                c.AllocatedAt,
            })
            .ToListAsync(cancellationToken);

        var payeeIds = raw.Select(c => c.PayeeId).Distinct().ToList();
        var payeeLookup = await db.Payees
            .Where(p => payeeIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, p.EmployeeCode })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var grouped = raw
            .GroupBy(c => c.PayeeId)
            .Select(g =>
            {
                payeeLookup.TryGetValue(g.Key, out var payee);
                var totals = g
                    .GroupBy(c => c.CreditedCurrency)
                    .Select(cg => new CurrencyTotalDto(cg.Sum(c => c.CreditedAmount), cg.Key))
                    .OrderBy(t => t.Currency)
                    .ToList();

                return new CreditByPayeeDto(
                    PayeeId: g.Key,
                    PayeeName: payee?.FullName,
                    PayeeCode: payee?.EmployeeCode,
                    CreditCount: g.Count(),
                    Totals: totals,
                    LatestAllocatedAt: g.Max(c => c.AllocatedAt));
            })
            .OrderByDescending(p => p.Totals.FirstOrDefault()?.Amount ?? 0)
            .ToList();

        return Result<IReadOnlyList<CreditByPayeeDto>>.Success(grouped);
    }
}
