using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Transactions;

public sealed class ExportTransactionsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    ITransactionExcelExportService excelService)
    : IRequestHandler<ExportTransactionsQuery, Result<ExportResult>>
{
    private const int MaxExportRows = 50_000;

    public async Task<Result<ExportResult>> Handle(
        ExportTransactionsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsExport, cancellationToken);

        var p = request.Filter;
        var query = db.CompensationTransactions.AsQueryable();

        // Apply the same filter predicates as ListTransactionsHandler.
        // (shared predicate helper deferred; duplication is acceptable at current scale.)

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

        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<CompensationTransactionStatus>(p.Status, ignoreCase: true, out var singleStatus))
            query = query.Where(t => t.Status == singleStatus);

        if (p.DateFrom.HasValue)
            query = query.Where(t => t.TransactionDate >= p.DateFrom.Value);

        if (p.DateTo.HasValue)
            query = query.Where(t => t.TransactionDate <= p.DateTo.Value);

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

        if (!string.IsNullOrWhiteSpace(p.ReferenceNumbers))
        {
            var refList = p.ReferenceNumbers.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            if (refList.Count > 0)
                query = query.Where(t => refList.Contains(t.ReferenceNumber));
        }

        if (p.PayeeId.HasValue)
            query = query.Where(t => t.PayeeId == (Guid?)p.PayeeId.Value);

        if (!string.IsNullOrWhiteSpace(p.Currencies))
        {
            var curs = p.Currencies.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => s.Length == 3)
                .ToList();
            if (curs.Count > 0)
                query = query.Where(t => curs.Contains(t.Amount.Currency));
        }

        // Safety cap: reject exports that would return too many rows.
        var count = await query.CountAsync(cancellationToken);
        if (count > MaxExportRows)
            return Result<ExportResult>.Failure(
                $"EXPORT_TOO_LARGE:{count}");

        // Sort by transaction date desc (stable order for the file).
        query = query.OrderByDescending(t => t.TransactionDate).ThenBy(t => t.ReferenceNumber);

        var transactions = await query.ToListAsync(cancellationToken);

        // Batch-fetch payee names in a single query.
        var payeeIds = transactions
            .Where(t => t.PayeeId.HasValue)
            .Select(t => t.PayeeId!.Value)
            .Distinct()
            .ToList();
        var payeeLookup = payeeIds.Count > 0
            ? await db.Payees
                .Where(pp => payeeIds.Contains(pp.Id))
                .Select(pp => new PayeeProjection(pp.Id, pp.FullName, pp.EmployeeCode))
                .ToDictionaryAsync(pp => pp.Id, cancellationToken)
            : new Dictionary<Guid, PayeeProjection>();

        var rows = transactions.Select(t =>
        {
            payeeLookup.TryGetValue(t.PayeeId ?? Guid.Empty, out var payee);
            return new TransactionExportRow(
                Id: t.Id,
                ReferenceNumber: t.ReferenceNumber,
                StaffId: payee?.EmployeeCode,
                PayeeName: payee?.FullName,
                Amount: t.Amount.Amount,
                Currency: t.Amount.Currency,
                TransactionDate: t.TransactionDate,
                Source: t.Source.ToString(),
                Status: t.Status.ToString(),
                CreatedAt: t.IngestedAt);
        }).ToList();

        // Resolve tenant slug for the filename from the tenant record.
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);
        var slug = tenant?.Slug ?? tenantContext.TenantId.ToString("N")[..8];

        var bytes = excelService.GenerateExcel(rows, slug);
        var fileName = $"transactions-export-{DateTime.UtcNow:yyyy-MM-dd}-{slug}.xlsx";

        return Result<ExportResult>.Success(
            new ExportResult(bytes, fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    }

    private sealed record PayeeProjection(Guid Id, string FullName, string? EmployeeCode);
}
