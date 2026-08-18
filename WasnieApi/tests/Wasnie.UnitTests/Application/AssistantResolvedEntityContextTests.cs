using System.Text.Json;
using FluentAssertions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Domain.Assistant;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// THE CHANNEL THAT CARRIES A RESOLVED ID FROM ONE TURN TO THE NEXT.
///
/// ★ THE DEFECT. Both payee-scoped tools accepted a <c>payeeId</c> "copied from an earlier answer in this
/// conversation", and the prompt ordered the model to do exactly that. It could never happen: the
/// dispatcher writes the arguments and reads only message TEXT, the tool payload holding the id is a
/// local that is dropped when the request ends, and rule 18 forbids an id appearing in a reply. The id
/// arguments were reachable only if the USER typed a GUID.
///
/// These tests are the proof that the channel exists, that only ids and names travel through it, and
/// that it does not become the "last entity" anchor that was rejected before — several payees coexist.
/// </summary>
public sealed class AssistantResolvedEntityContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Conversation = Guid.NewGuid();
    private static readonly Guid Tenant = Guid.NewGuid();

    private static AssistantMessage Turn(
        int sequence,
        AssistantMessageRole role = AssistantMessageRole.Assistant,
        string? payload = null,
        AssistantMessageStatus status = AssistantMessageStatus.Complete) =>
        AssistantMessage.Create(
            Guid.NewGuid(), Conversation, Tenant, role, $"turn {sequence}", sequence, Now,
            payload, status);

    /// <summary>The shape get_payee_balance really emits, trimmed to what matters here.</summary>
    private static string BalancePayload(Guid payeeId, string name) =>
        $$"""
        {"found":true,"payeeId":"{{payeeId}}","payeeName":"{{name}}","matchedBy":"ExactName",
         "period":"All time","balances":[{"currency":"EUR","earnedCommissions":78298.24,
         "outstandingDebt":0,"netPendingPayout":78298.24,"interpretation":"EarningsAndNoDebt"}]}
        """;

    private static string PlanRulesPayload(Guid planId, string name) =>
        $$"""
        {"outcome":"PlanRules","found":true,"matchedBy":"ExactName","planId":"{{planId}}",
         "planName":"{{name}}","planVersion":1,"planStatus":"Active","rules":[]}
        """;

    // ══ EXTRACTION: ONLY IDS AND NAMES LEAVE THE PAYLOAD ══════════════════════

    [Fact]
    public void The_balance_payload_yields_the_payee_and_nothing_else()
    {
        var id = Guid.NewGuid();

        var extracted = ResolvedEntityContext.Extract(BalancePayload(id, "Ana García"));

        extracted.Should().NotBeNull();
        extracted!.Payees.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ResolvedEntity(id, "Ana García"));
        extracted.Plans.Should().BeEmpty();
    }

    [Fact]
    public void The_plan_rules_payload_yields_the_plan()
    {
        var id = Guid.NewGuid();

        var extracted = ResolvedEntityContext.Extract(
            PlanRulesPayload(id, "Q3 2026 — Plan Comercial EMEA"));

        extracted!.Plans.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ResolvedEntity(id, "Q3 2026 — Plan Comercial EMEA"));
        extracted.Payees.Should().BeEmpty();
    }

    [Fact]
    public void NOT_ONE_FINANCIAL_FIGURE_REACHES_THE_DISPATCHER()
    {
        // ★★ THE RULE THE WHOLE DESIGN RESTS ON. Handing the router the raw payload would put balances,
        // debts and interpretation tokens in front of a classifier whose only job is to emit one
        // function call — and this router already has a documented failure mode where prose breaks the
        // parse. Two fields per entity is the entire contract, asserted by what is ABSENT.
        var block = ResolvedEntityContext.PromptBlock(
            ResolvedEntityContext.From([Turn(1, payload: ResolvedEntityContext.PayloadFor(
                BalancePayload(Guid.NewGuid(), "Ana García")))]));

        block.Should().Contain("Ana García");

        foreach (var leak in new[]
                 {
                     "78298", "earnedCommissions", "outstandingDebt", "netPendingPayout",
                     "EarningsAndNoDebt", "EUR", "balances", "matchedBy",
                 })
        {
            block.Should().NotContain(leak,
                "the dispatcher must receive identifiers, never the payload it came from");
        }
    }

    [Fact]
    public void A_refusal_resolves_nothing_and_is_remembered_as_nothing()
    {
        // Storing an entity for a lookup that found none would put a phantom in the next turn's context.
        ResolvedEntityContext.Extract("""{"found":false,"message":"No payee with that name was found."}""")
            .Should().BeNull();

        ResolvedEntityContext.PayloadFor("""{"found":false}""").Should().BeNull();
        ResolvedEntityContext.PayloadFor(null).Should().BeNull();
        ResolvedEntityContext.PayloadFor("not json at all").Should().BeNull();
    }

    [Fact]
    public void An_id_without_a_name_is_dropped_rather_than_stored_bare()
    {
        // A naked GUID cannot be matched against "Ana" by the dispatcher, so it can only ever be passed
        // by accident. The context's job is recognition, not storage.
        ResolvedEntityContext.Extract($$"""{"found":true,"payeeId":"{{Guid.NewGuid()}}"}""")
            .Should().BeNull();

        ResolvedEntityContext.Extract($$"""{"payeeId":"{{Guid.Empty}}","payeeName":"Nobody"}""")
            .Should().BeNull();
    }

    [Fact]
    public void Nested_plan_names_inside_an_assignment_list_are_NOT_harvested()
    {
        // ★ TOP-LEVEL SCALARS ONLY. Walking into arrays would make the context grow with the SIZE of an
        // answer — a payee on twelve plans would push twelve entities at the router — which is the
        // raw-payload problem arriving by a side door. The tool states its subject at the top level.
        var payeeId = Guid.NewGuid();
        var payload = $$"""
            {"outcome":"PayeePlans","found":true,"payeeId":"{{payeeId}}","payeeName":"Ana García",
             "assignments":[{"planId":"{{Guid.NewGuid()}}","planName":"Nested Plan"}]}
            """;

        var extracted = ResolvedEntityContext.Extract(payload);

        extracted!.Payees.Should().ContainSingle().Which.Id.Should().Be(payeeId);
        extracted.Plans.Should().BeEmpty("nested entities are deliberately not harvested");
    }

    // ══ THE ROUND TRIP THROUGH AssistantMessage.Payload ═══════════════════════

    [Fact]
    public void What_is_stored_on_a_turn_is_what_comes_back_from_the_thread()
    {
        var id = Guid.NewGuid();
        var stored = ResolvedEntityContext.PayloadFor(BalancePayload(id, "Ana García"));

        stored.Should().NotBeNull();

        var recovered = ResolvedEntityContext.From([Turn(0, AssistantMessageRole.User), Turn(1, payload: stored)]);

        recovered.Payees.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ResolvedEntity(id, "Ana García"));
    }

    [Fact]
    public void The_context_is_namespaced_so_a_foreign_payload_is_ignored_rather_than_misread()
    {
        // The Payload column is shared ground — RAG references and screen context are already planned
        // for it. Another piece's object is not ours and must not be parsed as if it were.
        var foreign = Turn(1, payload: """{"screenContext":{"route":"/payees"}}""");

        ResolvedEntityContext.From([foreign]).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_turn_that_looked_nothing_up_keeps_a_null_payload()
    {
        // "No lookup happened" and "a lookup found nothing" stay distinguishable in the row: an empty
        // object would assert that a lookup ran.
        ResolvedEntityContext.ToPayload(ResolvedEntities.None).Should().BeNull();
    }

    // ══ IT IS NOT A "LAST ENTITY" POINTER ═════════════════════════════════════

    [Fact]
    public void TWO_PAYEES_RESOLVED_IN_TWO_TURNS_BOTH_SURVIVE_newest_first()
    {
        // ★★ THE PROPERTY THAT MAKES COMPARISONS POSSIBLE, and the reason a mutable server-side "current
        // entity" was rejected. Ask about Ana, then about Bruno: an anchor holding the newest would have
        // destroyed Ana, and "compare the two" becomes a question about one person.
        var ana = Guid.NewGuid();
        var bruno = Guid.NewGuid();

        var resolved = ResolvedEntityContext.From(
        [
            Turn(1, payload: ResolvedEntityContext.PayloadFor(BalancePayload(ana, "Ana García"))),
            Turn(3, payload: ResolvedEntityContext.PayloadFor(BalancePayload(bruno, "Bruno Díaz"))),
        ]);

        resolved.Payees.Select(p => p.Id).Should().Equal([bruno, ana],
            "newest first — an implicit reference means the turn just before, and the older entry stays");
    }

    [Fact]
    public void A_payee_resolved_twice_appears_once()
    {
        var ana = Guid.NewGuid();

        var resolved = ResolvedEntityContext.From(
        [
            Turn(1, payload: ResolvedEntityContext.PayloadFor(BalancePayload(ana, "Ana García"))),
            Turn(3, payload: ResolvedEntityContext.PayloadFor(BalancePayload(ana, "Ana García"))),
        ]);

        resolved.Payees.Should().ContainSingle();
    }

    [Fact]
    public void Payees_and_plans_are_capped_independently_so_one_kind_cannot_crowd_out_the_other()
    {
        var turns = Enumerable.Range(1, ResolvedEntityContext.MaxPerKind + 3)
            .Select(i => Turn(i, payload: ResolvedEntityContext.PayloadFor(
                BalancePayload(Guid.NewGuid(), $"Payee {i}"))))
            .Append(Turn(0, payload: ResolvedEntityContext.PayloadFor(
                PlanRulesPayload(Guid.NewGuid(), "An Old Plan"))))
            .ToList();

        var resolved = ResolvedEntityContext.From(turns);

        resolved.Payees.Should().HaveCount(ResolvedEntityContext.MaxPerKind);
        resolved.Plans.Should().ContainSingle(
            "a full payee list must not evict the plan — the caps are per kind, and the loop cannot stop "
            + "until BOTH are full");
    }

    [Fact]
    public void A_CANCELLED_TURN_KEEPS_ITS_IDS()
    {
        // ★ The user stopped the PROSE; the lookup behind it ran and really did resolve a payee.
        // Dropping the id here would mean the next question after a Stop quietly falls back to retyping
        // the name — the exact failure this channel exists to end, on the one path where the user has
        // already shown they are in a hurry. (The TEXT of that turn is still filtered out of the
        // answering prompt, which is a different rule in a different file.)
        var ana = Guid.NewGuid();

        var resolved = ResolvedEntityContext.From(
        [
            Turn(1, payload: ResolvedEntityContext.PayloadFor(BalancePayload(ana, "Ana García")),
                status: AssistantMessageStatus.Cancelled),
        ]);

        resolved.Payees.Should().ContainSingle().Which.Id.Should().Be(ana);
    }

    [Fact]
    public void Nothing_resolved_means_no_block_at_all()
    {
        // An empty section announcing that there is no context is a section teaching the model to
        // expect one.
        ResolvedEntityContext.PromptBlock(ResolvedEntities.None).Should().BeEmpty();
        ResolvedEntityContext.From([Turn(1)]).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void The_block_is_valid_json_between_its_markers()
    {
        var block = ResolvedEntityContext.PromptBlock(
            ResolvedEntityContext.From([Turn(1, payload: ResolvedEntityContext.PayloadFor(
                BalancePayload(Guid.NewGuid(), "Ana García")))]));

        block.Should().StartWith(ResolvedEntityContext.Header);
        block.Should().EndWith(ResolvedEntityContext.Footer);

        var json = block
            .Replace(ResolvedEntityContext.Header, string.Empty)
            .Replace(ResolvedEntityContext.Footer, string.Empty)
            .Trim();

        var parsed = JsonDocument.Parse(json).RootElement;
        parsed.GetProperty("payees").EnumerateArray().Single()
            .GetProperty("name").GetString().Should().Be("Ana García");
    }

    [Fact]
    public void A_non_ascii_name_survives_unescaped()
    {
        // Same reason the tools relax the encoder: the destination is a prompt, and a Spanish or Polish
        // name arriving as a wall of escape sequences is a name the router has to read through.
        var block = ResolvedEntityContext.PromptBlock(
            ResolvedEntityContext.From([Turn(1, payload: ResolvedEntityContext.PayloadFor(
                BalancePayload(Guid.NewGuid(), "Zoë Wysoczańska")))]));

        block.Should().Contain("Zoë Wysoczańska").And.NotContain("\\u");
    }
}
