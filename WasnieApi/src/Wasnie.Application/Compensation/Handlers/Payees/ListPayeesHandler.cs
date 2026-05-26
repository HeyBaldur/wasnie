using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payees;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Application.Common.Extensions;

namespace Wasnie.Application.Compensation.Handlers.Payees;

public sealed class ListPayeesHandler(IApplicationDbContext db)
    : IRequestHandler<ListPayeesQuery, Result<PagedResult<PayeeDto>>>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase) { "fullname", "employeecode", "hiredate", "role" };

    public async Task<Result<PagedResult<PayeeDto>>> Handle(ListPayeesQuery request, CancellationToken cancellationToken)
    {
        var p = request.Pagination;
        var query = db.Payees.AsQueryable();

        // Search
        if (!string.IsNullOrWhiteSpace(p.Search))
        {
            var q = p.Search.Trim().ToLower();
            query = query.Where(x =>
                x.FullName.ToLower().Contains(q) ||
                x.EmployeeCode.ToLower().Contains(q) ||
                x.Email.ToLower().Contains(q));
        }

        // Filters
        if (p.Filters != null)
        {
            if (p.Filters.TryGetValue("status", out var statusStr) &&
                int.TryParse(statusStr, out var statusInt))
                query = query.Where(x => x.Status == (PayeeStatus)statusInt);

            if (p.Filters.TryGetValue("managerid", out var mgrIdStr) &&
                Guid.TryParse(mgrIdStr, out var mgrId))
                query = query.Where(x => x.ManagerId == mgrId);
        }

        // Sort
        var sortBy = AllowedSortFields.Contains(p.SortBy ?? "") ? p.SortBy!.ToLower() : "fullname";
        var desc = string.Equals(p.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        query = sortBy switch
        {
            "employeecode" => desc ? query.OrderByDescending(x => x.EmployeeCode) : query.OrderBy(x => x.EmployeeCode),
            "hiredate"     => desc ? query.OrderByDescending(x => x.HireDate)     : query.OrderBy(x => x.HireDate),
            "role"         => desc ? query.OrderByDescending(x => x.Role)          : query.OrderBy(x => x.Role),
            _              => desc ? query.OrderByDescending(x => x.FullName)      : query.OrderBy(x => x.FullName),
        };

        var paged = await query.ToPagedResultAsync(p.Page, p.PageSize, cancellationToken);

        var payeeIds = paged.Items.Select(x => x.Id).ToList();
        var assignmentCounts = await db.PlanAssignments
            .Where(a => payeeIds.Contains(a.PayeeId) && a.Status == AssignmentStatus.Active)
            .GroupBy(a => a.PayeeId)
            .Select(g => new { PayeeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PayeeId, x => x.Count, cancellationToken);

        var managerIds = paged.Items.Where(x => x.ManagerId.HasValue).Select(x => x.ManagerId!.Value).Distinct().ToList();
        var managers = managerIds.Count > 0
            ? await db.Payees.IgnoreQueryFilters()
                .Where(x => managerIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x, cancellationToken)
            : new Dictionary<Guid, Payee>();

        var dtos = paged.Items.Select(payee =>
        {
            managers.TryGetValue(payee.ManagerId ?? Guid.Empty, out var mgr);
            return CreatePayeeHandler.ToDto(
                payee,
                assignmentCounts.GetValueOrDefault(payee.Id),
                mgr?.FullName,
                mgr?.EmployeeCode);
        }).ToList();

        return Result<PagedResult<PayeeDto>>.Success(new PagedResult<PayeeDto>
        {
            Items = dtos,
            TotalCount = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
        });
    }
}
