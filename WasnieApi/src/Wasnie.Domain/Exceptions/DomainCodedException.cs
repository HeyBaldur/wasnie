namespace Wasnie.Domain.Exceptions;

/// <summary>
/// A domain refusal the reader will see, expressed as a CODE AND ITS DATA rather than as a sentence.
///
/// ★★ THE PLAIN <see cref="DomainException"/> IS AN ENGLISH SENTENCE, AND THAT IS WHY THIS EXISTS.
/// Its <c>Message</c> travels to the browser and is painted straight into a toast, so a product that
/// ships in EN, ES and PL shows one of them an English sentence and the other two the same English
/// sentence. Fixing a Polish wording in that design needs a backend redeploy, and the backend is the
/// one place that does not know who is reading.
///
/// ★ THE REPO ALREADY DECIDED THIS. <c>PayoutSkipReason</c> and <c>RuleSimulationBlocker</c> both
/// emit codes the front end looks up; this type is the same decision applied to the ERROR path,
/// where <see cref="Result{T}.Failure"/>'s single string had left no room for it.
///
/// ★ THE PARAMETERS ARE STRUCTURED, NOT INTERPOLATED. "Tier 2 ends at 10000" cannot be translated
/// once the number is already inside the sentence: Spanish and Polish put it elsewhere. So the code
/// says WHICH refusal and the parameters say WITH WHAT, and only the translation file joins them.
///
/// ★ IT CARRIES NO USER-FACING PROSE AT ALL. <see cref="Exception.Message"/> is set to the code so
/// that logs and stack traces stay readable, but that value is an identifier and must never reach a
/// screen — <c>ExceptionHandlingMiddleware</c> deliberately does not send it as <c>message</c>, and a
/// client that ignores <c>code</c> falls back to its own generic error rather than printing this.
/// </summary>
public sealed class DomainCodedException : DomainException
{
    /// <summary>The invariant that was broken. Matched by the client against its own translations.</summary>
    public string Code { get; }

    /// <summary>
    /// The values the sentence needs, by name. Numbers stay numbers: they are echoed back into the
    /// same fields the user typed them into, so they must not be pre-formatted for a locale here.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; }

    public DomainCodedException(string code, IReadOnlyDictionary<string, object?>? parameters = null)
        : base(code)
    {
        Code = code;
        Parameters = parameters ?? new Dictionary<string, object?>();
    }
}
