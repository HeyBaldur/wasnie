using Wasnie.Application.Authorization;

namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// Resource-level authorisation for payee data: not "may this ROLE read ledgers" (that is
/// <see cref="IAuthorizationService"/>) but "may this USER read THIS payee's ledger".
///
/// ★ WHY THE TWO ARE SEPARATE AND BOTH ARE NEEDED. A permission answers a question about a verb; this
/// answers a question about an object. Rep and Manager both hold Ledger.Read — legitimately, because
/// seeing why your own pay shrank is the product — and for a long time that was the ONLY check on
/// /api/payees/{id}/ledger/statement. Any Rep could substitute any payee id and read a colleague's
/// pay. The permission was never wrong; it was answering a different question.
/// </summary>
public interface IPayeeAccessGuard
{
    /// <summary>
    /// The set of payees the asking user may read. One database round trip at most, and
    /// <see cref="PayeeVisibility.None"/> on every path that cannot be resolved.
    /// </summary>
    Task<PayeeVisibility> GetVisibilityAsync(CancellationToken cancellationToken = default);

    /// <summary>Shorthand for <c>(await GetVisibilityAsync()).Allows(payeeId)</c>.</summary>
    Task<bool> CanReadAsync(Guid payeeId, CancellationToken cancellationToken = default);
}
