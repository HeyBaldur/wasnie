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
    int PageSize = 25,
    /// <summary>
    /// A partial sale reference. Matches anywhere in the reference, case-insensitively.
    ///
    /// ★ LAST IN THE LIST BECAUSE THE OTHERS ARE POSITIONAL. Several call sites already construct
    /// this record with positional arguments (the controller, the export, a dozen tests); inserting a
    /// parameter in the middle would silently re-bind Page and PageSize to the wrong values, and it
    /// would still compile.
    /// </summary>
    string? Reference = null);

public sealed record GetReconciliationQuery(ReconciliationFilter Filter)
    : IRequest<Result<ReconciliationPageDto>>;

public sealed record ExportReconciliationQuery(ReconciliationFilter Filter)
    : IRequest<Result<ExportResult>>;
