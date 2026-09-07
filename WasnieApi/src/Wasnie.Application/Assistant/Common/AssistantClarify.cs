using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// Which state a clarify form is in. Written by the person who answers or closes it, never by the model.
/// </summary>
public static class ClarifyState
{
    /// <summary>Shown, and nobody has acted on it yet.</summary>
    public const string Open = "open";

    /// <summary>The user picked one of the options. It stays on screen as a record of what they chose.</summary>
    public const string Answered = "answered";

    /// <summary>The user closed it because none of the options helped. Not an error, and not a failure.</summary>
    public const string Dismissed = "dismissed";

    public static bool IsKnown(string? value) =>
        value is Open or Answered or Dismissed;
}

/// <summary>
/// One option the assistant offers: a real function, named by the tool that implements it.
///
/// ★★ THE NAME IS A TOOL NAME, NOT A LABEL. The screen translates it through a whitelist, exactly the
/// way audit actions and reconciliation reasons are translated — an option that reached a user reading
/// <c>get_payee_balance</c> would be an internal identifier on screen, which rule 10a already forbids.
/// Sending a label instead would put English in the payload of a Spanish user's stored conversation.
/// </summary>
/// <param name="Function">The registered tool name this option runs.</param>
/// <param name="Argument">
/// The value the assistant already has for that function's main parameter — a payee's name, a plan's
/// name, a reference — or null when it has none and must ask for one.
///
/// ★ IT IS WHAT THE USER ALREADY SAID, NEVER SOMETHING INFERRED — on a FUNCTION form. The whole point
/// of KAN-58 is that a plausible value is not a value; an option that pre-fills a guessed name would
/// run a real lookup on invented input and return a real-looking answer about the wrong record.
///
/// ★★ ON AN ENTITY FORM IT IS THE OPPOSITE, AND THAT IS NOT A LOOSENING OF THE RULE. There the value
/// did not come from the model at all: a lookup RAN, matched several records, and each option carries
/// the identifier of one of them straight out of the database. The anti-invention rule is "where did
/// this come from", not "did the user type it", and a resolved row is the strongest possible answer.
/// </param>
/// <param name="Entity">
/// What this option IS, when the choice is between records rather than between functions. Null on a
/// function form, where the label is the function's own name.
/// </param>
public sealed record ClarifyOption(string Function, string? Argument, ClarifyEntity? Entity = null);

/// <summary>
/// How one candidate record is described on an entity form.
///
/// ★★ THREE FIELDS BECAUSE ONE IS NOT ENOUGH TO CHOOSE ON, and that is the reported failure in
/// miniature. A tenant answering "Camille" holds Camille Laurent (EPO9009, terminated), Camille
/// Laurent (EMP409, active) and Camille Martin (FR-301) — two of them share a full name, so a list of
/// names alone offers a choice nobody can make. The code separates the namesakes and the status is
/// usually the thing the reader actually knows about the person they mean.
///
/// ★ IT IS STRUCTURED RATHER THAN A PRE-BUILT LABEL STRING, so the screen can translate the status and
/// order the parts for its own language. A server-composed "Camille Laurent · EPO9009 · Active" would
/// put an English word inside a Spanish user's stored conversation, which is the same defect §C1 names
/// about prose reaching the screen.
///
/// ★ AND IT CARRIES NO MONEY AND NO ID. The user has not yet said which record they mean, so nothing
/// may be shown that belongs to a specific one of them; the id stays off the page under rule 10b.
/// </summary>
/// <param name="Name">The record's own display name — the FULL one, never the fragment searched for.</param>
/// <param name="Code">What distinguishes namesakes: an employee code, a plan version. May be null.</param>
/// <param name="Status">A status TOKEN for the screen to translate, never a translated word.</param>
public sealed record ClarifyEntity(string Name, string? Code, string? Status);

/// <summary>What a clarify form asks the user to choose BETWEEN.</summary>
public static class ClarifyKind
{
    /// <summary>Which of my functions did you want? The original form; the default when absent.</summary>
    public const string Function = "function";

    /// <summary>Which of these records did you mean? Same function on every option.</summary>
    public const string Entity = "entity";

    public static bool IsKnown(string? value) => value is Function or Entity;
}

/// <summary>
/// The form the assistant shows instead of guessing.
/// </summary>
/// <param name="Options">
/// The functions worth offering for THIS question. Never the whole catalogue.
///
/// ★★ EMPTY IS NOT A VALID FORM, AND THAT IS THE RULE FROM THE TICKET'S THIRD COMMENT. When no
/// function is relevant — "what is the weather?" — the assistant answers honestly and shows NO form.
/// Offering "look up a transaction" to somebody asking about the weather is a menu that insults the
/// question. So a form with no options is never written; see <see cref="ClarifyForm.IsOfferable"/>.
/// </param>
/// <param name="State">See <see cref="ClarifyState"/>. Always <c>open</c> when first written.</param>
/// <param name="Kind">See <see cref="ClarifyKind"/>. Null means a function form, which is the original.</param>
/// <param name="TotalMatches">
/// How many records actually matched, when that is MORE than the options listed.
///
/// ★★ IT EXISTS SO NOTHING IS LOST IN SILENCE. A name like "García" can belong to fifteen people; the
/// panel shows the first few, and a panel that showed a few while implying it showed all would be the
/// same class of lie this ticket is about — a confident partial answer. Null when every match is on
/// the form, so the screen says nothing extra in the ordinary case.
/// </param>
public sealed record ClarifyForm(
    IReadOnlyList<ClarifyOption> Options,
    string State,
    string? Kind = null,
    int? TotalMatches = null)
{
    /// <summary>
    /// The most FUNCTIONS a form may offer.
    ///
    /// ★ THREE, BECAUSE THE POINT IS A SHORTLIST. There are five functions; a form offering four or
    /// five of them has stopped reasoning about the question and is showing a catalogue, which is the
    /// "menú tonto" the ticket's first comment rules out by name. If a question genuinely touches more
    /// than three, it is too vague to shortlist and the free-text box is the better answer.
    /// </summary>
    public const int MaxOptions = 3;

    /// <summary>
    /// The most RECORDS an entity form may list.
    ///
    /// ★★ IT IS A DIFFERENT NUMBER BECAUSE IT ANSWERS A DIFFERENT ARGUMENT, and collapsing the two
    /// would have broken one of them. Three is right for functions: a fourth function on the form
    /// means the shortlist was not thought about. Three is WRONG for records — the candidates are not
    /// a shortlist somebody reasoned into, they are everyone who bears the name, and cutting them to
    /// three would hide real people behind a cap chosen to discipline a model. Eight fits the panel
    /// above the composer without scrolling; past that the honest move is to say how many there are
    /// and let the user narrow the name, which is what <see cref="TotalMatches"/> carries.
    /// </summary>
    public const int MaxEntityOptions = 8;

    public bool IsEntityForm => string.Equals(Kind, ClarifyKind.Entity, StringComparison.Ordinal);

    /// <summary>How many options this KIND of form may carry.</summary>
    public int Cap => IsEntityForm ? MaxEntityOptions : MaxOptions;

    /// <summary>A form is worth showing only when it has at least one real function behind it.</summary>
    public bool IsOfferable => Options.Count > 0;
}

/// <summary>
/// The clarify form's channel: from the model's tool call, into the turn's row, back out to the screen.
///
/// ★★ IT RIDES IN <see cref="Wasnie.Domain.Assistant.AssistantMessage.Payload"/> UNDER ITS OWN KEY, and
/// that column is shared ground by design — <see cref="ResolvedEntityContext.PayloadKey"/> already
/// occupies "resolvedEntities" and its comment says the namespacing exists precisely so the next piece
/// has somewhere to go. So this needs no migration and no new column, and the two coexist in one object
/// rather than one overwriting the other. <see cref="Merge"/> is what keeps that true.
///
/// ★ THE FORM BELONGS TO ONE MESSAGE, NOT TO THE CONVERSATION AND NOT TO THE USER. That is the ticket's
/// third comment expressed as storage: a new chat starts clean because a new chat has no message
/// carrying one, and a refresh restores the state because the state is a column on that row.
/// </summary>
public static class AssistantClarify
{
    /// <summary>The key this occupies inside the shared payload object.</summary>
    public const string PayloadKey = "clarify";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Reads a clarify form out of a tool payload, or returns null when the turn produced none.
    ///
    /// ★ SAME SHAPE AS ResolvedEntityContext.Extract, AND FOR THE SAME REASON: the tool runner's
    /// contract stays exactly as it is. A tool returns JSON; whoever cares about part of it reads that
    /// part. Adding a second return channel to <see cref="AssistantToolOutcome"/> would have made every
    /// other tool carry a field about a feature none of them participate in.
    /// </summary>
    public static ClarifyForm? Extract(string? toolPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(toolPayloadJson)) return null;

        try
        {
            using var document = JsonDocument.Parse(toolPayloadJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!document.RootElement.TryGetProperty(PayloadKey, out var node)) return null;

            var form = node.Deserialize<ClarifyForm>(Json);

            // A form the model emitted with no options is not shown, per the ticket's third comment:
            // no relevant function means an honest sentence, not an empty panel.
            return form is null || !form.IsOfferable ? null : Normalise(form);
        }
        catch (JsonException)
        {
            // A malformed payload is not a crash and not a form. The turn still has its answer.
            return null;
        }
    }

    /// <summary>
    /// Trims a form to what may actually be shown.
    ///
    /// ★ THE CAP IS ENFORCED HERE, NOT ASKED FOR IN THE PROMPT. A prompt asking for "at most three" is
    /// a request; this is the thing that makes it true. Same argument as FabricationGuard: the wording
    /// lowers the rate, the code sets the bound.
    /// </summary>
    /// <remarks>
    /// ★★ THE DEDUPE KEY IS THE FUNCTION **AND** ITS ARGUMENT, AND THAT CHANGE IS WHAT MAKES ENTITY
    /// FORMS POSSIBLE AT ALL. It used to be the function alone, which was right while every option
    /// named a different one — and it silently destroyed the case this ticket now asks for: three
    /// options all running get_payee_balance for EPO9009, EMP409 and FR-301 are ONE function and three
    /// records, so the old key collapsed them to a single button pointing at whichever came first.
    /// A form asking "which of these three people?" would have shown one person.
    ///
    /// The separator is a NUL because it cannot occur in a tool name or an employee code, so no pair of
    /// distinct options can be made to collide by concatenation.
    /// </remarks>
    private static ClarifyForm Normalise(ClarifyForm form)
    {
        var kind = ClarifyKind.IsKnown(form.Kind) ? form.Kind : null;

        var options = form.Options
            .Where(o => !string.IsNullOrWhiteSpace(o.Function))
            .DistinctBy(o => $"{o.Function}\u0000{o.Argument}", StringComparer.Ordinal)
            .ToList();

        var cap = (form with { Kind = kind }).Cap;

        return form with
        {
            Kind = kind,
            Options = options.Take(cap).ToList(),
            State = ClarifyState.IsKnown(form.State) ? form.State : ClarifyState.Open,

            // ★ THE COUNT SURVIVES THE TRIM, WHICH IS THE ONLY REASON THE TRIM IS ACCEPTABLE. If the
            // cap dropped candidates, the form has to keep saying how many there really were — the
            // sender's own figure when it gave one, and otherwise what it handed us before the cut.
            TotalMatches = form.TotalMatches is int declared && declared > options.Count
                ? declared
                : options.Count > cap ? options.Count : form.TotalMatches,
        };
    }

    /// <summary>
    /// The form for "which of these records did you mean?", built by a TOOL that resolved a name to
    /// more than one row.
    ///
    /// ★★ THIS IS THE HALF THAT MAKES THE FEATURE DETERMINISTIC, AND IT IS WHY IT LIVES HERE RATHER
    /// THAN IN THE MODEL'S HANDS. The function form is chosen by the dispatcher, so it is only ever as
    /// reliable as a classifier — the defect this ticket spent two rounds on. This one is not chosen at
    /// all: a lookup ran, it matched N rows, and N > 1 IS the form. No prompt can fail to trigger it and
    /// no sampling can vary it, which is exactly what the 12:29 comment asked for.
    ///
    /// ★ AND IT IS GENERIC BY CONSTRUCTION. It knows nothing about payees: the caller supplies the
    /// options, because only the caller knows which identifier re-runs ITS lookup — an employee code
    /// for a payee, a name for a plan, a reference for a transaction. Anything that can resolve a name
    /// to several rows can offer this form without a line being added here.
    ///
    /// ★ NOTHING IS OFFERED FOR ONE CANDIDATE. A single match is an answer, not a question, and the
    /// resolver already returns it directly — a form with one button would ask the user to confirm
    /// something the system already knew.
    /// </summary>
    /// <param name="options">One per candidate record, each carrying the identifier that resolves it.</param>
    /// <param name="totalMatches">
    /// How many matched in total. When it exceeds what is listed, the screen says so — see
    /// <see cref="ClarifyForm.TotalMatches"/> on why silence there would be its own small lie.
    /// </param>
    public static ClarifyForm? EntityForm(IReadOnlyList<ClarifyOption> options, int totalMatches)
    {
        if (options.Count < 2) return null;

        var listed = options.Take(ClarifyForm.MaxEntityOptions).ToList();
        var total = Math.Max(totalMatches, options.Count);

        return new ClarifyForm(
            listed,
            ClarifyState.Open,
            ClarifyKind.Entity,
            total > listed.Count ? total : null);
    }

    /// <summary>The form serialised as the fragment that lives under <see cref="PayloadKey"/>.</summary>
    public static string? ToPayload(ClarifyForm? form) =>
        form is null || !form.IsOfferable
            ? null
            : JsonSerializer.Serialize(
                new Dictionary<string, ClarifyForm> { [PayloadKey] = form }, Json);

    /// <summary>The clarify fragment for a turn, straight from its tool payload.</summary>
    public static string? PayloadFor(string? toolPayloadJson) => ToPayload(Extract(toolPayloadJson));

    /// <summary>
    /// Combines the fragments two independent features each want to store on the same turn.
    ///
    /// ★★ WITHOUT THIS THEY OVERWRITE EACH OTHER, SILENTLY AND IN A WAY NO TEST OF EITHER WOULD SEE.
    /// Both <see cref="ResolvedEntityContext.ToPayload"/> and <see cref="ToPayload"/> serialise a
    /// COMPLETE object with one key in it, so persisting whichever ran last would drop the other — and
    /// losing the resolved entities does not break anything visibly: it just quietly makes the next
    /// turn ask for a name the thread had already resolved.
    ///
    /// ★ NULLS ARE THE COMMON CASE. Most turns have neither; many have one. Returning null for "both
    /// empty" keeps the column null on ordinary turns, which is what every existing row looks like.
    /// </summary>
    public static string? Merge(params string?[] fragments)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var fragment in fragments)
        {
            if (string.IsNullOrWhiteSpace(fragment)) continue;

            try
            {
                using var document = JsonDocument.Parse(fragment);
                if (document.RootElement.ValueKind != JsonValueKind.Object) continue;

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    // Clone: the JsonDocument is disposed at the end of this block and its elements
                    // die with it. Without the clone the serialisation below reads freed memory.
                    merged[property.Name] = property.Value.Clone();
                }
            }
            catch (JsonException)
            {
                // One unreadable fragment must not cost the other one its place in the row.
            }
        }

        return merged.Count == 0 ? null : JsonSerializer.Serialize(merged, Json);
    }

    /// <summary>
    /// The same payload with its clarify form moved to a new state, or null when there is nothing to
    /// move. Everything else in the payload is carried through untouched.
    /// </summary>
    public static string? WithState(string? payload, string state)
    {
        var form = Extract(payload);
        if (form is null) return payload;

        return Merge(payload, ToPayload(form with { State = state }));
    }
}
