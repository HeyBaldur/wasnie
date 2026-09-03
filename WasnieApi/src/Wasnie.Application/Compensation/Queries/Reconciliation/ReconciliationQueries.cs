using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Reconciliation;

/// <summary>
/// The filter behind both the queue and its totals.
///
/// ★ ONE FILTER OBJECT, TWO ENDPOINTS. The export takes the same shape as the list, because "export
/// the filtered set" is only true if the two agree on what filtered means.
/// </summary>
public sealed record ReconciliationFilter(
    Guid? PayeeId = null,
    string? Reason = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int PageSize = 25);

public sealed record GetReconciliationQuery(ReconciliationFilter Filter)
    : IRequest<Result<ReconciliationPageDto>>;

public sealed record ExportReconciliationQuery(ReconciliationFilter Filter)
    : IRequest<Result<ExportResult>>;
