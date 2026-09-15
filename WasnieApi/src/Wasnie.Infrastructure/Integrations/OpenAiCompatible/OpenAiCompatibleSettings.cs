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
/// <param name="Routing">
/// Which upstream vendors an AGGREGATOR may hand the request to (KAN-74). Null = the aggregator's default routing; a vendor
/// that serves its own models (Groq) has nothing to route, so it is always null there.
/// </param>
public sealed record OpenAiCompatibleSettings(
    string ApiKey,
    string BaseUrl,
    string Model,
    string GenerationModel,
    int TimeoutSeconds,
    string HttpClientName,
    ProviderRouting? Routing = null);

/// <summary>
/// OpenRouter's `provider` request object: which vendors may serve the call (KAN-74).
///
/// ★★ A DATA-PROTECTION SETTING AS MUCH AS A SPEED ONE. Each upstream vendor is a sub-processor of the payroll data in
/// the prompt (docs/Legal.md §5). OpenRouter's default routing picks the CHEAPEST host — measured serving our calls from
/// AkashML and Darkbloom, 6 to 11 s each, vendors nobody chose or declared. `Only` names the vendors allowed, and
/// OpenRouter never uses one outside it: there is no silent fallback to "whoever is cheapest".
/// </summary>
/// <param name="Only">Vendor slugs allowed to serve the call (e.g. <c>cerebras</c>). Empty = no restriction.</param>
/// <param name="Order">Preference order among the allowed vendors.</param>
/// <param name="AllowFallbacks">Whether the next vendor may be tried when one fails — only ever within <paramref name="Only"/>.</param>
/// <param name="Zdr">Restrict to Zero Data Retention endpoints on every request, not only through the account toggle.</param>
/// <param name="RequireParameters">Skip vendors that do not support every parameter sent (JSON mode for the router, tools
/// for the dispatcher) instead of letting one silently ignore it.</param>
public sealed record ProviderRouting(
    IReadOnlyList<string> Only,
    IReadOnlyList<string> Order,
    bool AllowFallbacks,
    bool Zdr,
    bool RequireParameters)
{
    /// <summary>Null when nothing is configured, so the request carries no `provider` object at all.</summary>
    public static ProviderRouting? From(
        IReadOnlyList<string>? only, IReadOnlyList<string>? order, bool allowFallbacks, bool zdr, bool requireParameters)
    {
        var allowed = (only ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        var preferred = (order ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

        return allowed.Count == 0 && preferred.Count == 0 && !zdr && !requireParameters
            ? null
            : new ProviderRouting(allowed, preferred, allowFallbacks, zdr, requireParameters);
    }
}
