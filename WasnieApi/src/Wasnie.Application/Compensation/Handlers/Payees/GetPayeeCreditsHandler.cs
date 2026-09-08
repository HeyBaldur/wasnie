using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Helpers;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Credits;
using Wasnie.Application.Compensation.Queries.Credits;
using Wasnie.Application.Compensation.Queries.Payees;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Payees;

public sealed class GetPayeeCreditsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IClock clock)
    : IRequestHandler<GetPayeeCreditsQuery, Result<PagedResult<CreditListDto>>>
{
    public async Task<Result<PagedResult<CreditListDto>>> Handle(
        GetPayeeCreditsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.CreditsRead, cancellationToken);

        var payeeId = request.PayeeId;
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var (defaultFrom, defaultTo) = DashboardRangeHelper.DefaultRange(today);
        var from = request.From ?? defaultFrom;
        var to = request.To ?? defaultTo;

        var query = db.Credits
            .Where(c => c.PayeeId == payeeId && c.SupersededAt == null);

        // ★★ SCOPED BY AllocatedAt — CHANGED FROM THE TRANSACTION'S DATE (KAN-63).
        //
        // This list used to filter on the underlying transaction's TransactionDate. Two things were
        // wrong with that. It disagreed with everywhere else the product counts commission — the main
        // credits screen filters on allocation, and so do the Total/Paid/Unpaid cards — so the same
        // payee's commission for the same range had two different answers depending on which screen
        // asked. And a transaction date MOVES: a CRM deal's close date can change after the fact, which
        // is what the drift alerts report, so money would silently relocate between ranges after it had
        // been read. Allocation is the moment the commission came into existence and cannot move.
        //
        // The cards sitting directly above this list are built from the same field, so the rows here
        // are the rows those figures are made of.
        var fromDto = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDto = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        query = query.Where(c => c.AllocatedAt >= fromDto && c.AllocatedAt <= toDto);

        var totalCount = await query.CountAsync(cancellationToken);
        var credits = await query
            .OrderByDescending(c => c.AllocatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = await ListCreditsHandler.EnrichPageAsync(db, credits, new CreditFilterQuery(), cancellationToken);

        return Result<PagedResult<CreditListDto>>.Success(new PagedResult<CreditListDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
        });
    }
}
