using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Extensions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Transactions;

public sealed class ListTransactionsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<ListTransactionsQuery, Result<PagedResult<TransactionDto>>>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "transactiondate", "amount", "status", "ingestedat", "referencenumber"
        };

    public async Task<Result<PagedResult<TransactionDto>>> Handle(
        ListTransactionsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsRead, cancellationToken);

        var p = request.Pagination;
        var query = db.CompensationTransactions.AsQueryable();

        // Filters
        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<CompensationTransactionStatus>(p.Status, ignoreCase: true, out var statusEnum))
            query = query.Where(t => t.Status == statusEnum);

        if (p.PayeeId.HasValue)
            query = query.Where(t => t.PayeeId == (Guid?)p.PayeeId.Value);

        if (!string.IsNullOrWhiteSpace(p.Source) &&
            Enum.TryParse<TransactionSource>(p.Source, ignoreCase: true, out var sourceEnum))
            query = query.Where(t => t.Source == sourceEnum);

        if (p.DateFrom.HasValue)
            query = query.Where(t => t.TransactionDate >= p.DateFrom.Value);

        if (p.DateTo.HasValue)
            query = query.Where(t => t.TransactionDate <= p.DateTo.Value);

        // Sort
        var sortBy = AllowedSortFields.Contains(p.SortBy ?? "") ? p.SortBy!.ToLower() : "transactiondate";
        var desc = string.Equals(p.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        query = sortBy switch
        {
            "amount" => desc ? query.OrderByDescending(t => t.Amount.Amount) : query.OrderBy(t => t.Amount.Amount),
            "status" => desc ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            "ingestedat" => desc ? query.OrderByDescending(t => t.IngestedAt) : query.OrderBy(t => t.IngestedAt),
            "referencenumber" => desc ? query.OrderByDescending(t => t.ReferenceNumber) : query.OrderBy(t => t.ReferenceNumber),
            _ => desc ? query.OrderByDescending(t => t.TransactionDate) : query.OrderBy(t => t.TransactionDate),
        };

        var paged = await query.ToPagedResultAsync(p.Page, p.PageSize, cancellationToken);

        // Batch-fetch payee names for this page in a single query — no N+1.
        // Tenant-scoped automatically via the global query filter on Payees.
        var payeeIds = paged.Items
            .Select(t => t.PayeeId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var payeeLookup = await db.Payees
            .Where(p => payeeIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, p.EmployeeCode })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var dtos = paged.Items.Select(t =>
        {
            var payee = t.PayeeId.HasValue ? payeeLookup.GetValueOrDefault(t.PayeeId.Value) : null;
            return IngestTransactionHandler.ToDto(t) with
            {
                PayeeName = payee?.FullName,
                PayeeEmployeeCode = payee?.EmployeeCode,
            };
        }).ToList();

        return Result<PagedResult<TransactionDto>>.Success(new PagedResult<TransactionDto>
        {
            Items = dtos,
            TotalCount = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
        });
    }
}
