using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Extensions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payees;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;

namespace Wasnie.Application.Compensation.Handlers.Payees;

/// <summary>
/// The payee list (KAN-92, batch 4).
///
/// ★★ THE PERMISSION WAS NEVER THE WHOLE ANSWER. Payees.Read says "this role may read payee data";
/// it does not say WHICH payees, and for a long time this handler asked only the first question. A
/// Rep holds Payees.Read legitimately — they have to see their own record — and so every Rep could
/// page through the entire company: names, employee codes and EMAIL ADDRESSES of every colleague.
/// The per-resource endpoints were already guarded; a list cannot be, because it takes no payee id.
/// It has to be FILTERED, which is why PayeeVisibility carries a set and not a boolean.
///
/// ★ AN EMPTY SET IS AN EMPTY PAGE, NOT AN ERROR. Somebody whose account is not linked to a payee
/// sees no rows and a total of zero — the same answer as a company with no payees, which is correct:
/// from where they stand there is nothing to list. It is not a refusal, so it raises no alert.
/// </summary>
public sealed class ListPayeesHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IAuthorizationService authorizationService,
    IPayeeAccessGuard payeeAccessGuard)
    : IRequestHandler<ListPayeesQuery, Result<PagedResult<PayeeDto>>>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase) { "fullname", "employeecode", "hiredate", "role" };

    public async Task<Result<PagedResult<PayeeDto>>> Handle(ListPayeesQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayeesRead, cancellationToken);
        var p = request.Pagination;
        var query = db.Payees.AsQueryable();

        // ★ The narrowing goes FIRST, before search, filters and paging, so the total count is the
        // count of what this reader may see. Applying it later would page over other people's rows and
        // report a total that includes them — the number itself would disclose the roster.
        var visibility = await payeeAccessGuard.GetVisibilityAsync(cancellationToken);
        if (!visibility.IsUnrestricted)
        {
            var visibleIds = visibility.PayeeIds;
            query = query.Where(x => visibleIds.Contains(x.Id));
        }

        // Search
        if (!string.IsNullOrWhiteSpace(p.Search))
        {
            var q = p.Search.Trim().ToLower();
            query = query.Where(x =>
                x.FullName.ToLower().Contains(q) ||
                x.EmployeeCode.ToLower().Contains(q) ||
                (x.Email != null && x.Email.ToLower().Contains(q)));
        }

        // Filters
        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<PayeeStatus>(p.Status, ignoreCase: true, out var statusEnum))
            query = query.Where(x => x.Status == statusEnum);

        if (p.ManagerId.HasValue)
            query = query.Where(x => x.ManagerId == p.ManagerId);

        // Sort
        var sortBy = AllowedSortFields.Contains(p.SortBy ?? "") ? p.SortBy!.ToLower() : "fullname";
        var desc = string.Equals(p.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        query = sortBy switch
        {
            "employeecode" => desc ? query.OrderByDescending(x => x.EmployeeCode) : query.OrderBy(x => x.EmployeeCode),
            "hiredate" => desc ? query.OrderByDescending(x => x.HireDate) : query.OrderBy(x => x.HireDate),
            "role" => desc ? query.OrderByDescending(x => x.Role) : query.OrderBy(x => x.Role),
            _ => desc ? query.OrderByDescending(x => x.FullName) : query.OrderBy(x => x.FullName),
        };

        var paged = await query.ToPagedResultAsync(p.Page, p.PageSize, cancellationToken);

        var payeeIds = paged.Items.Select(x => x.Id).ToList();
        var assignmentCounts = await db.PlanAssignments
            .Where(a => payeeIds.Contains(a.PayeeId) && a.Status == AssignmentStatus.Active)
            .GroupBy(a => a.PayeeId)
            .Select(g => new { PayeeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PayeeId, x => x.Count, cancellationToken);

        var managerIds = paged.Items.Where(x => x.ManagerId.HasValue).Select(x => x.ManagerId!.Value).Distinct().ToList();
        var currentTenantId = tenantContext.TenantId;
        var managers = managerIds.Count > 0
            ? await db.Payees.IgnoreQueryFilters()
                .Where(x => x.TenantId == currentTenantId && managerIds.Contains(x.Id))
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
