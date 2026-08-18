using System.Text.Json;
using System.Text.Json.Serialization;
using Wasnie.Domain.Assistant;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// ★★ THE CHANNEL THAT CARRIES A RESOLVED IDENTIFIER FROM ONE TURN TO THE NEXT.
///
/// ★ THE DEFECT THIS EXISTS TO FIX, AND IT WAS STRUCTURAL. Both payee-scoped tools accept a
/// <c>payeeId</c> "copied from an earlier answer in this conversation", and rules 10b/10c ordered the
/// model to do exactly that. Neither could ever work, for four reasons that only bite in combination:
///
///   1. The component that WRITES tool arguments is the DISPATCHER
///      (<see cref="AssistantToolRunner.SelectAsync"/>), not the model that composes the answer.
///   2. The dispatcher is shown the stored conversation and nothing else — message TEXT, truncated.
///   3. The tool payload, the one place the id ever appears, is a local variable. It is built, put in
///      the answering prompt for that single turn, and dropped. Nothing persists it.
///   4. Rule 18 forbids the assistant printing an id in its reply — correctly, they are internal.
///
/// So the id was never in the text, and the text was the only thing the next turn could read. The id
/// arguments were reachable only if the USER typed a GUID. What "worked" was the dispatcher recopying
/// the NAME from context, which is the retyping the ids were introduced to eliminate.
///
/// ★ ONLY IDS AND CANONICAL NAMES TRAVEL — NEVER THE RAW PAYLOAD. Handing the dispatcher the balance
/// payload would put currency totals, debts and interpretation tokens in front of a classifier whose
/// entire job is to emit one function call. A router shown financial figures starts answering about
/// them instead of routing, and this one already has a documented failure mode where a friendly
/// preamble breaks the parse. Two fields per entity is the whole contract.
///
/// ★ STATELESS WITH RESPECT TO THE DATABASE — no new table, no new column, no migration. It rides on
/// <see cref="AssistantMessage.Payload"/>, a nullable JSON column that already exists, is already
/// migrated, and was added for precisely this ("structure that later pieces attach to a turn"). The
/// context is DERIVED from the thread on every turn rather than maintained anywhere.
///
/// ★ AND IT IS NOT THE "LAST ENTITY" ANCHOR THAT WAS REJECTED BEFORE. That objection was to a mutable
/// server-side pointer holding the newest entity, which silently destroys comparisons — ask about Ana,
/// then about Bruno, and Ana is gone. This is append-only: each turn records what IT resolved, the
/// context is rebuilt by reading BACK over the turns, and several payees coexist. Nothing is ever
/// overwritten, so there is no state to drift.
/// </summary>
public sealed record ResolvedEntity(Guid Id, string Name);

/// <summary>
/// What a conversation has resolved so far, newest first. Both lists may be empty; the whole thing is
/// absent from the prompt when nothing has been looked up.
/// </summary>
public sealed record ResolvedEntities(
    IReadOnlyList<ResolvedEntity> Payees,
    IReadOnlyList<ResolvedEntity> Plans)
{
    public static readonly ResolvedEntities None = new([], []);

    public bool IsEmpty => Payees.Count == 0 && Plans.Count == 0;
}

/// <summary>
/// Reads ids out of a tool payload, stores them on the turn, and rebuilds them from the thread.
/// </summary>
public static class ResolvedEntityContext
{
    /// <summary>
    /// How many entities of each kind travel. Four covers a comparison of two payees with room to
    /// spare; an unbounded list would grow the dispatcher's window on every turn of a long thread,
    /// which is the cost this design exists to avoid paying.
    /// </summary>
    public const int MaxPerKind = 4;

    /// <summary>
    /// The key the context occupies inside <see cref="AssistantMessage.Payload"/>. Namespaced because
    /// that column is deliberately shared ground — RAG references and screen context are already
    /// planned for it, and a bare object at the root would leave the next piece nowhere to go.
    /// </summary>
    public const string PayloadKey = "resolvedEntities";

    public const string Header = "=== ENTITIES ALREADY RESOLVED IN THIS CONVERSATION ===";

    public const string Footer = "=== END OF RESOLVED ENTITIES ===";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        // Same reasoning as the tools' own serialiser: the only destination is a prompt, and a Spanish
        // or Polish name must not arrive as a wall of escape sequences.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The entities a tool payload resolved, or null when it resolved none.
    ///
    /// ★ TOP-LEVEL SCALARS ONLY, deliberately. Every tool that resolves an entity states it as a
    /// first-class field of its answer — <c>payeeId</c>/<c>payeeName</c>, <c>planId</c>/<c>planName</c>
    /// — because that is what the payload contract already requires. Walking into nested arrays would
    /// harvest every plan named inside a payee's assignment list, and a context that grows with the
    /// SIZE of an answer is the raw-payload problem arriving by a side door.
    ///
    /// A refusal payload has no id and yields null, which is the correct outcome: nothing was resolved,
    /// so nothing should be remembered as resolved.
    /// </summary>
    public static ResolvedEntities? Extract(string? toolPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(toolPayloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(toolPayloadJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var payee = ReadEntity(document.RootElement, "payeeId", "payeeName");
            var plan = ReadEntity(document.RootElement, "planId", "planName");

            if (payee is null && plan is null)
            {
                return null;
            }

            return new ResolvedEntities(
                payee is null ? [] : [payee],
                plan is null ? [] : [plan]);
        }
        catch (JsonException)
        {
            // A tool that emitted something unparseable has a bigger problem than this, and it is not
            // this method's to report. No context is better than a guessed one.
            return null;
        }
    }

    /// <summary>
    /// The context serialised for <see cref="AssistantMessage.Payload"/>, or null when there is
    /// nothing to store — a turn that resolved nothing keeps a null payload rather than an empty
    /// object, so "no lookup happened" and "a lookup found nothing" stay distinguishable in the row.
    /// </summary>
    public static string? ToPayload(ResolvedEntities? entities)
    {
        if (entities is null || entities.IsEmpty)
        {
            return null;
        }

        return JsonSerializer.Serialize(
            new Dictionary<string, ResolvedEntities> { [PayloadKey] = entities }, Json);
    }

    /// <summary>Extraction and serialisation in one step, for the two answer handlers.</summary>
    public static string? PayloadFor(string? toolPayloadJson) => ToPayload(Extract(toolPayloadJson));

    /// <summary>
    /// Everything the thread has resolved, NEWEST FIRST, deduplicated by id.
    ///
    /// ★ NEWEST FIRST IS THE INSTRUCTION, NOT A SORT PREFERENCE. "and what plans does he have?" refers
    /// to the payee of the previous turn, and a list whose head is the most recent resolution makes
    /// that the obvious reading without a rule having to state it. The older entries stay so that
    /// "compare her with Bruno" still has Bruno.
    ///
    /// ★ CANCELLED TURNS COUNT. A stopped answer is filtered out of the ANSWERING prompt, because its
    /// words break off mid-thought and would be imitated. The lookup behind it, however, genuinely ran
    /// and genuinely resolved a real payee — the user stopped the prose, not the query. Dropping the id
    /// would mean a follow-up after a Stop silently falls back to retyping the name.
    /// </summary>
    public static ResolvedEntities From(IEnumerable<AssistantMessage> history)
    {
        var payees = new List<ResolvedEntity>();
        var plans = new List<ResolvedEntity>();
        var seenPayees = new HashSet<Guid>();
        var seenPlans = new HashSet<Guid>();

        foreach (var message in history.OrderByDescending(m => m.Sequence))
        {
            if (payees.Count >= MaxPerKind && plans.Count >= MaxPerKind)
            {
                break;
            }

            var stored = ReadPayload(message.Payload);

            if (stored is null)
            {
                continue;
            }

            Accumulate(stored.Payees, payees, seenPayees);
            Accumulate(stored.Plans, plans, seenPlans);
        }

        return payees.Count == 0 && plans.Count == 0
            ? ResolvedEntities.None
            : new ResolvedEntities(payees, plans);
    }

    /// <summary>
    /// The block the dispatcher is shown, or empty when nothing has been resolved — an empty section
    /// announcing that there is no context is a section teaching the model to expect one.
    /// </summary>
    public static string PromptBlock(ResolvedEntities entities) =>
        entities.IsEmpty
            ? string.Empty
            : $"{Header}\n{JsonSerializer.Serialize(entities, Json)}\n{Footer}";

    private static void Accumulate(
        IReadOnlyList<ResolvedEntity> source, List<ResolvedEntity> target, HashSet<Guid> seen)
    {
        foreach (var entity in source)
        {
            if (target.Count >= MaxPerKind || !seen.Add(entity.Id))
            {
                continue;
            }

            target.Add(entity);
        }
    }

    private static ResolvedEntities? ReadPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(PayloadKey, out var stored))
            {
                // A payload written by some other piece for some other purpose. Not ours, not an error.
                return null;
            }

            return stored.Deserialize<ResolvedEntities>(Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ResolvedEntity? ReadEntity(JsonElement root, string idField, string nameField)
    {
        if (!root.TryGetProperty(idField, out var idValue)
            || !Guid.TryParse(idValue.GetString(), out var id)
            || id == Guid.Empty)
        {
            return null;
        }

        var name = root.TryGetProperty(nameField, out var nameValue)
            ? nameValue.GetString()?.Trim()
            : null;

        // ★ THE NAME IS NOT OPTIONAL, and an id without one is dropped rather than stored bare. The
        // context's job is to let the dispatcher recognise WHICH entity a sentence refers to; a naked
        // GUID it cannot match against "Ana" is a token of noise that can only be passed by accident.
        return string.IsNullOrWhiteSpace(name) ? null : new ResolvedEntity(id, name);
    }
}
