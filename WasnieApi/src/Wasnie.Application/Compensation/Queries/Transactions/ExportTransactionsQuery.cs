using MediatR;
using Wasnie.Application.Common.Models;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Transactions;

/// <summary>
/// Generates an Excel (.xlsx) file containing ALL rows that match the given filter
/// (no pagination — export returns the full result set up to the safety cap).
/// </summary>
public sealed record ExportTransactionsQuery(PaginationQuery Filter) : IRequest<Result<ExportResult>>;

public sealed record ExportResult(byte[] Bytes, string FileName, string ContentType);
