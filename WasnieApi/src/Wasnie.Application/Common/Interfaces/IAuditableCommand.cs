namespace Wasnie.Application.Common.Interfaces;

public interface IAuditableCommand
{
    string AuditAction { get; }
    string AuditResourceType { get; }
    string? AuditResourceId { get; }
    string? AuditDisplayName { get; }

    /// <summary>
    /// Typed detail for the audit row — the WHAT, beside the who and the when.
    ///
    /// ★★ WHY IT WAS ADDED. AuditLog has carried a Metadata column all along, but nothing on this
    /// interface could fill it, so every command routed through AuditBehavior produced an entry with
    /// an action and a display name and nothing else. For a money-critical command that is not enough:
    /// "a departed payee's account was closed" does not answer "which credits, for how much" six months
    /// later, and a free-text note is not a query
    /// (docs/DIAG_ORPHAN_ACCOUNT_CLOSURE.md §4.1).
    ///
    /// ★ DEFAULTED TO NULL ON PURPOSE. Every existing command keeps compiling and keeps behaving
    /// exactly as before; only a command that has something worth recording overrides it.
    ///
    /// It is read AFTER the handler runs, so a command whose detail is only knowable post-execution can
    /// still be described — but prefer detail the request already carries and the handler VERIFIED,
    /// which is stronger evidence than something computed on the way out.
    /// </summary>
    Dictionary<string, string>? AuditMetadata => null;
}
