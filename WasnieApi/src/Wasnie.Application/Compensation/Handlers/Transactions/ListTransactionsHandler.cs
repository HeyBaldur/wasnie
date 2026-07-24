using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Extensions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Common;
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

        // Unfiltered total (whole tenant) — counted before any filter is applied.
        var unfilteredTotal = await db.CompensationTransactions.CountAsync(cancellationToken);

        var query = db.CompensationTransactions.AsQueryable();

        // ── Dashboard "needs attention" deep-link: filter to one unprocessable-Pending reason ──
        // Uses the SAME spec as the dashboard counts, so the list count matches the card exactly.
        if (!string.IsNullOrWhiteSpace(p.AttentionReason))
        {
            var reasonQuery = UnprocessablePendingSpec.ForReason(db, p.AttentionReason.Trim());
            if (reasonQuery is not null)
                query = reasonQuery;
        }

        // ── Legacy single-value filters (backward compat) ──────────────────────

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

        // ── Extended filters (WI-PROD-I.2) ─────────────────────────────────────

        if (!string.IsNullOrWhiteSpace(p.Reference))
        {
            var refLower = p.Reference.Trim().ToLower();
            query = query.Where(t => t.ReferenceNumber.ToLower().Contains(refLower));
        }

        if (!string.IsNullOrWhiteSpace(p.Statuses))
        {
            var statusList = p.Statuses.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => Enum.TryParse<CompensationTransactionStatus>(s, ignoreCase: true, out _))
                .Select(s => Enum.Parse<CompensationTransactionStatus>(s, ignoreCase: true))
                .ToList();
            if (statusList.Count > 0)
                query = query.Where(t => statusList.Contains(t.Status));
        }

        if (!string.IsNullOrWhiteSpace(p.PayeeIds))
        {
            var payeeIdList = p.PayeeIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Guid.TryParse(s.Trim(), out var g) ? (Guid?)g : null)
                .Where(g => g.HasValue)
                .Select(g => g!.Value)
                .ToList();
            if (payeeIdList.Count > 0)
                query = query.Where(t => t.PayeeId.HasValue && payeeIdList.Contains(t.PayeeId.Value));
        }

        if (p.IngestedFrom.HasValue)
            query = query.Where(t => t.IngestedAt >= p.IngestedFrom.Value);

        if (p.IngestedTo.HasValue)
            query = query.Where(t => t.IngestedAt <= p.IngestedTo.Value);

        if (p.AmountMin.HasValue)
            query = query.Where(t => t.Amount.Amount >= p.AmountMin.Value);

        if (p.AmountMax.HasValue)
            query = query.Where(t => t.Amount.Amount <= p.AmountMax.Value);

        if (p.UnassignedOnly == true)
            query = query.Where(t => t.PayeeId == null);

        // WI-ENRICHMENT: informational filter — Pending transactions with no resolved category.
        // Composes with the other filters; it never restricts processing, only what the list shows.
        if (p.UncategorizedOnly == true)
            query = query.Where(t =>
                t.Status == CompensationTransactionStatus.Pending && t.Category == null);

        if (!string.IsNullOrWhiteSpace(p.ReferenceNumbers))
        {
            var refList = p.ReferenceNumbers.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            if (refList.Count > 0)
                query = query.Where(t => refList.Contains(t.ReferenceNumber));
        }

        if (!string.IsNullOrWhiteSpace(p.Currencies))
        {
            var curs = p.Currencies.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => s.Length == 3)
                .ToList();
            if (curs.Count > 0)
                query = query.Where(t => curs.Contains(t.Amount.Currency));
        }

        // ── Sort ────────────────────────────────────────────────────────────────

        // AmountSort overrides SortBy when present.
        if (!string.IsNullOrWhiteSpace(p.AmountSort))
        {
            var amountDesc = string.Equals(p.AmountSort, "desc", StringComparison.OrdinalIgnoreCase);
            query = amountDesc
                ? query.OrderByDescending(t => t.Amount.Amount)
                : query.OrderBy(t => t.Amount.Amount);
        }
        else
        {
            var sortBy = AllowedSortFields.Contains(p.SortBy ?? "") ? p.SortBy!.ToLower() : "ingestedat";
            var desc = !string.Equals(p.SortOrder, "asc", StringComparison.OrdinalIgnoreCase);

            query = sortBy switch
            {
                "amount" => desc ? query.OrderByDescending(t => t.Amount.Amount) : query.OrderBy(t => t.Amount.Amount),
                "status" => desc ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
                "ingestedat" => desc ? query.OrderByDescending(t => t.IngestedAt) : query.OrderBy(t => t.IngestedAt),
                "referencenumber" => desc ? query.OrderByDescending(t => t.ReferenceNumber) : query.OrderBy(t => t.ReferenceNumber),
                _ => desc ? query.OrderByDescending(t => t.TransactionDate) : query.OrderBy(t => t.TransactionDate),
            };
        }

        var paged = await query.ToPagedResultAsync(p.Page, p.PageSize, cancellationToken);

        // Batch-fetch payee names for this page in a single query — no N+1.
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
            UnfilteredTotal = unfilteredTotal,
        });
    }
}
