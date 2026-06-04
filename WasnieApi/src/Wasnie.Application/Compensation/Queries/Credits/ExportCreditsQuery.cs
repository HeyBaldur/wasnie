using MediatR;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Credits;

/// <summary>
/// Returns an .xlsx file containing all Credits matching the given filter (no pagination).
/// </summary>
public sealed record ExportCreditsQuery(CreditFilterQuery Filter)
    : IRequest<Result<ExportResult>>;
