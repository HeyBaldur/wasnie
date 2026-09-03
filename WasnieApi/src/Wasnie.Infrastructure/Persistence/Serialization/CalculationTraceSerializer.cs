using System.Text.Json;
using System.Text.Json.Serialization;
using Wasnie.Application.Compensation.Calculation;

namespace Wasnie.Infrastructure.Persistence.Serialization;

/// <summary>
/// Turns the engine's <see cref="RuleCalculationTrace"/> into the document stored on
/// <c>Credits.CalculationTrace</c>, and back.
///
/// ★★ ONE OPTIONS OBJECT, SHARED BY THE WRITER AND EVERY FUTURE READER. Two JsonSerializerOptions
/// built in two places agree until one of them is edited, and the failure is silent: the document
/// still parses, a field just quietly stops round-tripping. That is not a tolerable failure mode for
/// the record of how somebody's pay was computed, so both directions go through here.
///
/// ★ THE ENUMS ARE WRITTEN AS TEXT, and that is the decision this file exists to hold. The compact
/// form — enums as their ordinal — ties years of stored history to today's declaration order, so
/// inserting one member into <see cref="RuleCalculationOutcome"/> would silently reinterpret every
/// trace ever written: "the cap was skipped" becomes "the cap applied", retroactively, with nothing
/// to notice it by. Text costs bytes and cannot do that.
///
/// ★ AND UNKNOWN MEMBERS DO NOT THROW ON READ. A trace written by a later version may carry an
/// outcome this build has never heard of; refusing to parse the whole document because of one
/// unfamiliar word would take a payment breakdown offline over a word. See <see cref="Deserialize"/>.
/// </summary>
public static class CalculationTraceSerializer
{
    /// <summary>The current shape. Written into every document; see <c>RuleCalculationTrace._schema</c>.</summary>
    public const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions Options = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new MoneyJsonConverter());
        // Verbose on purpose — see the type comment. Not negotiable for a stored audit document.
        opts.Converters.Add(new JsonStringEnumConverter());
        // A step carries a scalar only where its component has one; writing a wall of nulls would
        // triple the document for nothing anybody reads.
        opts.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return opts;
    }

    /// <summary>
    /// The document to store. Never null: a credit exists, so it was computed, and a computation
    /// that produced a payment always has a cascade to show.
    /// </summary>
    public static string Serialize(RuleCalculationTrace trace) =>
        JsonSerializer.Serialize(trace, Options);

    /// <summary>
    /// Reads a stored document back.
    ///
    /// ★ NULL IN, NULL OUT, AND THAT IS A REAL ANSWER. Every credit allocated before this column
    /// existed has no trace, and never will — the inputs are gone. "We did not record this" is the
    /// honest reply and it must not be confused with an empty cascade, which would read as an engine
    /// that ran and did nothing.
    /// </summary>
    public static RuleCalculationTrace? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        return JsonSerializer.Deserialize<RuleCalculationTrace>(json, Options);
    }
}
