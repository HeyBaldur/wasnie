namespace Wasnie.Infrastructure.Integrations.OpenAiCompatible;

/// <summary>
/// The whole of what one vendor contributes to an OpenAI-compatible chat provider.
///
/// A record rather than an interface because there is nothing to implement — every field is a value
/// read from configuration. It exists so the base provider can be written once against "a vendor"
/// instead of against Groq, and so adding a third one is a settings object and a registration.
/// </summary>
/// <param name="Model">
/// The model for the SHORT, STRUCTURED calls: the section router and the tool dispatcher. Both are
/// classification — pick a section, pick a tool — and a small fast model does them well and cheaply.
/// </param>
/// <param name="GenerationModel">
/// The model that WRITES THE ANSWER, and the only one whose output a user ever reads.
///
/// ★ SEPARATE BECAUSE THE TWO JOBS FAIL DIFFERENTLY. A router that picks a slightly worse section
/// costs a slightly worse answer. A generator that breaks produces the failure that opened this WI:
/// gpt-oss-20b fell into a repetition loop mid-explanation — "mandatorio mandatorio mandatorio" for
/// hundreds of words — on screen, in a product that tells people what they are paid. Buying robustness
/// where it is read and thrift where it is not is the whole reason this is two fields.
/// </param>
/// <param name="HttpClientName">
/// The named <c>HttpClient</c>, one per vendor. Sharing one would mean a timeout tuned for one
/// endpoint silently applying to the other.
/// </param>
public sealed record OpenAiCompatibleSettings(
    string ApiKey,
    string BaseUrl,
    string Model,
    string GenerationModel,
    int TimeoutSeconds,
    string HttpClientName);
