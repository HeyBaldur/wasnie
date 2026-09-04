namespace Wasnie.Domain.Common.Results;

/// <summary>
/// The success/failure face of a <see cref="Result"/> or <see cref="Result{T}"/>, with the generic
/// argument erased.
///
/// ★★ WHY IT EXISTS (KAN-34). <see cref="Result"/> and <see cref="Result{T}"/> are two sealed classes
/// with no common ancestor, so a MediatR pipeline behavior — which only ever sees an open
/// <c>TResponse</c> — had no way to ask "did this command actually succeed?". It did not ask, and
/// wrote an audit row for every command that returned <c>Result.Failure</c> without throwing: 10 rows
/// in AuditLogs claim actions that never happened, including four reversals of a single 2.980 EUR
/// commission that was reverted once.
///
/// ★ NOT A REFACTOR OF Result. Both types already exposed exactly these two members; this interface
/// only names them. No call site changes, no behaviour changes, and a pattern match against it stays
/// correct if a third Result shape ever appears.
/// </summary>
public interface IResultOutcome
{
    bool IsSuccess { get; }
    string? Error { get; }
}
