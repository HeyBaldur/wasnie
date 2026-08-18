namespace Wasnie.Application.Authorization;

/// <summary>
/// The ONE answer given both to "there is no such payee" and to "that payee is not yours".
///
/// ★ ONE CONSTANT, NOT TWO THAT HAPPEN TO READ THE SAME. If a caller can tell the two apart, the API
/// is an enumeration oracle: a Rep walks the id space and learns exactly which ids are real, which is
/// most of what they wanted from the ledger endpoint anyway. Two separate string literals would drift
/// the first time somebody improved the wording of one of them — the leak would come back through a
/// copy-editing change, with no test failing. So there is one symbol, and both call sites use it.
///
/// The same reasoning the assistant's tools already apply to transaction lookups
/// (GetTransactionTool.RefusalMessage), applied at the layer underneath them.
///
/// The HTTP shape matters as much as the text: both must map to the SAME status code. The controllers
/// return 404 for this message — never 403, which would itself confirm the payee exists.
/// </summary>
public static class PayeeAccessDenied
{
    public const string Message = "Payee not found.";

    /// <summary>
    /// For resources reached by their OWN id rather than the payee's — a quota id, an assignment id.
    /// The payee is discovered from the loaded row, so the refusal must impersonate that endpoint's
    /// existing not-found answer rather than the payee one, or the caller learns which of the two
    /// happened from the wording alone.
    /// </summary>
    public const string QuotaMessage = "Quota not found.";

    public const string AssignmentMessage = "Assignment not found.";
}
