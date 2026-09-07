using MediatR;
using Wasnie.Application.Audit.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Audit.Queries;

/// <summary>A page of the tenant's audit trail (KAN-19).</summary>
public sealed record GetAuditLogsQuery(AuditLogFilter Filter)
    : IRequest<Result<AuditLogPageDto>>;

/// <summary>The evidence behind one row.</summary>
public sealed record GetAuditLogDetailQuery(long Id)
    : IRequest<Result<AuditLogDetailDto>>;

/// <summary>
/// The vocabulary the filters offer, read from the log itself.
///
/// ★ SERVED, NOT HARD-CODED IN THE CLIENT — the same rule the Reconciliation Centre's reason list
/// follows. What the page can FILTER BY is whatever the log contains; what it can TRANSLATE is a
/// separate, smaller question the front answers with its own whitelist.
/// </summary>
public sealed record GetAuditLogFilterOptionsQuery
    : IRequest<Result<AuditLogFilterOptionsDto>>;
