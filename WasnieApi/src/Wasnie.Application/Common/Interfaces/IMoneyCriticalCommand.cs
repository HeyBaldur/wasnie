namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// Marks a command as money-critical, requiring atomic audit+business commit.
/// The AuditBehavior wraps these in an explicit DB transaction: if the audit
/// write fails, the business write rolls back and the exception propagates.
///
/// Use on all Phase 2+ commands that touch money:
/// Transactions, Payouts, Credits, commission recalculations.
///
/// Non-money commands use IAuditableCommand directly (audit failure swallowed).
/// </summary>
public interface IMoneyCriticalCommand : IAuditableCommand { }
