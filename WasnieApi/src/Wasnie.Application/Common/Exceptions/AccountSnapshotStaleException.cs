namespace Wasnie.Application.Common.Exceptions;

/// <summary>
/// The account moved between the moment the user was shown it and the moment they confirmed.
///
/// ★★ WHY THIS IS AN EXCEPTION AND NOT A FAILURE RESULT. It maps to 409, and 409 is the only status
/// that tells a client "your request was well formed, you were just looking at something else". A 400
/// would invite a retry with the same body; a silent success would close a set the user never saw.
/// Closing an account destroys a claim a departed person still had, so "close exactly what was shown,
/// or close nothing" is the whole contract.
///
/// ★ AND IT CANNOT BE A TOTAL COMPARISON. Two credits can change while the sum stays put — €500 arrives
/// as €500 is consumed — and the set is then a different set with the same total. The handler compares
/// ids AND amounts, in both directions.
///
/// A departed payee's credit set is genuinely unstable: the product deliberately allows a credit to
/// arrive after someone leaves, and one already did, 56 seconds after a termination
/// (docs/DIAG_POL-8554_PAYOUT_Y_CREDITOS_INVENTADOS.md).
/// </summary>
/// <param name="reason">
/// A code the client maps to a sentence — never printed raw. See <see cref="Reasons"/>.
/// </param>
/// <param name="message">What changed, in words, for the log and for a developer reading a trace.</param>
public sealed class AccountSnapshotStaleException(string reason, string message) : Exception(message)
{
    public string Reason { get; } = reason;

    /// <summary>The codes. Adding one means adding its EN/ES/PL translation in the same change.</summary>
    public static class Reasons
    {
        /// <summary>A credit the user did not see is now outstanding on this account.</summary>
        public const string CreditAppeared = "CreditAppeared";

        /// <summary>A credit the user saw is no longer outstanding — paid, superseded or already closed.</summary>
        public const string CreditDisappeared = "CreditDisappeared";

        /// <summary>A credit is still there but its amount is not the one that was shown.</summary>
        public const string CreditAmountChanged = "CreditAmountChanged";

        /// <summary>The ledger balance is not the figure the user was shown.</summary>
        public const string BalanceChanged = "BalanceChanged";
    }
}
