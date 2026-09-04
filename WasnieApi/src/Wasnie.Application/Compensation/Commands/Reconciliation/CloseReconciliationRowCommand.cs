using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Reconciliation;

/// <summary>
/// "Reviewed, and left as it stands": close one row of the Reconciliation Centre by human decision.
///
/// ★★ THE CLIENT SENDS THE ROW AND THE REASON IT WROTE — NOT THE FACTS. It names which row (kind +
/// entity) and why (the note), and nothing else. WHICH anomalies that row currently carries, and the
/// timestamp of each, are read by the handler from the live queue. A client that could state its own
/// <c>FactOccurredAt</c> could post a date far in the future and silence anomalies that have not
/// happened yet — the exclusion is load-bearing, so its inputs come from the server's own query.
///
/// ★ NOT <c>IMoneyCriticalCommand</c>. It moves no money and changes no credit: it writes one new
/// row beside the anomaly. It IS auditable — the informational entry the ticket asks for — and after
/// KAN-34 that entry only appears if this command actually succeeded.
/// </summary>
public sealed record CloseReconciliationRowCommand(
    ReconciliationEntryKind Kind,
    Guid EntityId,
    string Note) : IRequest<Result<CloseReconciliationRowResult>>, IAuditableCommand
{
    public string AuditAction => AuditActions.ReconciliationRowClosed;
    public string AuditResourceType => ResourceTypes.Reconciliation;
    public string? AuditResourceId => EntityId.ToString();
    public string? AuditDisplayName => Kind.ToString();
}

/// <summary>
/// What was closed. ★ THE REASONS ARE ECHOED BACK because closing a row closes the anomalies it
/// carried AT THAT MOMENT, and the caller cannot know that list — the server read it.
/// </summary>
public sealed record CloseReconciliationRowResult(
    Guid EntityId,
    ReconciliationEntryKind Kind,
    IReadOnlyList<string> ClosedReasons);
