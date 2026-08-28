using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;

namespace Wasnie.Application.Assistant.Tools;

/// <summary>
/// The assistant's second read-only tool: READ A PLAN'S ACTUAL RULES, as typed configuration.
///
/// ★ WHY IT EXISTS. Without it the assistant answered configuration questions by reasoning over the
/// handbook — and reasoning produced a rate mode nobody implemented, an explanation of unit-based pay as
/// if it were a percentage, and a worked calculation that quietly skipped the cap. Every one of those is
/// a plausible sentence about somebody's pay. With this tool the configuration is READ, and the model's
/// job drops from inferring the rules to rendering them in the user's language.
///
/// ★ THE PAYLOAD CARRIES TOKENS, NOT PROSE. See <see cref="RateSemantic"/>. The backend never writes the
/// explanation: an English sentence built here would need translating for the ES and PL users the
/// product already has, and putting presentation in the domain is how i18n collapses. The system prompt
/// teaches every token once; the model renders it.
///
/// ★ RULE 1 — THROUGH THE DOMAIN, WITH THE ASKING USER'S IDENTITY. <c>ListPlansQuery</c> then
/// <c>GetPlanByIdQuery</c>: the same two queries the plans screen sends, inside the caller's request
/// scope, so <c>Plans.Read</c> and the tenant query filter apply on their own. No DbContext in this
/// file, and there must never be one.
///
/// ★ RULE 3 — A REFUSAL SAYS NOTHING. "No such plan" and "exists, but not yours" return the IDENTICAL
/// payload. A TECHNICAL failure is the opposite: it is raised, so the runner fails the turn and the user
/// gets a retry card instead of being told their plan does not exist.
///
/// ★ NO PII. A plan's configuration — rates, caps, conditions — is not personal data, and nothing here
/// reaches for a payee: not a name, not an id, not an assignment. The one place user-entered text
/// survives is a rule name and a condition value, which are passed through exactly as the administrator
/// typed them.
/// </summary>
public sealed class GetPlanRulesTool(ISender sender, ILogger<GetPlanRulesTool> logger) : IAssistantTool
{
    public const string ToolName = "get_plan_rules";

    /// <summary>★ ONE SENTENCE, IDENTICAL FOR BOTH REFUSALS — see Rule 3 above.</summary>
    public const string RefusalMessage =
        "No plan with that name was found, or you do not have access to it.";

    /// <summary>
    /// How many plans are listed when resolving a name, and when listing what the user can see.
    /// A tenant with more plans than this is asked to name one rather than shown a truncated list that
    /// reads as complete.
    /// </summary>
    private const int MaxPlansListed = 50;

    /// <summary>Two plan names are the same name when they normalise to the same thing.</summary>
    private static readonly IEqualityComparer<string> PlanNameComparer = new NormalisedNameComparer();

    public AssistantToolSchema Schema { get; } = new(
        ToolName,
        "Read the real, current configuration of a compensation plan: every active rule, what triggers "
        + "it, what it measures, its rate table, and any modifier, cap or floor. Read-only. Use it "
        + "whenever the user asks how a plan pays, what rules a plan has, why their commission was a "
        + "particular amount, or how a plan is configured. Omit planName when the user did not name a "
        + "plan.",
        // ★ DELIBERATELY NOT STRICT, for the same reason get_transaction is not: `additionalProperties:
        // false` asks the PROVIDER to validate the generated call, and a failure there is a 400 for the
        // whole request instead of a refusal the user can read.
        """
        {
          "type": "object",
          "properties": {
            "planId": {
              "type": "string",
              "description": "The plan's id, copied EXACTLY from an earlier answer in this conversation. Always prefer this over planName when the plan has already been looked up in this session — retyping a title from memory is what produces 'no such plan' for a plan you just explained."
            },
            "planName": {
              "type": "string",
              "description": "The plan's COMPLETE name, copied verbatim from the user's message. Copy the WHOLE title including any leading period or quarter (for example 'Q3 2026 - ') and any trailing parenthetical (for example '(Test Integral)'). Do NOT shorten it, do not drop a prefix or a suffix, and do not tidy it up. Omit this argument entirely if the user did not name a plan."
            }
          },
          "required": []
        }
        """);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        // ★ THIS PAYLOAD IS READ BY A LANGUAGE MODEL, NOT BY A BROWSER. The default encoder escapes
        // every non-ASCII character for HTML safety, so a Spanish plan reached the model as
        // "Comisión Base Revenue" (Flat + Modifier)" — six characters where there was
        // one, on a product whose tenants write in Spanish and Polish. It inflates the context, and it
        // hands a small model a wall of escape sequences to read a rule name through.
        //
        // Safe here because the payload's only destination is the prompt: it is never embedded in HTML,
        // never in a URL, and never in a script tag. If that ever changes, this line changes with it.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<string> RunAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        var planName = ReadPlanName(argumentsJson);
        var planId = ReadPlanId(argumentsJson);

        // ── The id path: the plan was already looked up in this conversation ──
        //
        // ★ THE NAME IS A GUESS THE MODEL RETYPES; THE ID IS A FACT IT COPIES. This tool's whole history
        // is names arriving slightly wrong — an em-dash for a hyphen, a dropped quarter prefix — and a
        // user being told a plan they were looking at does not exist. PlanNameMatch made those survivable;
        // the id makes them not happen at all on the second and third turn.
        //
        // Authorisation is untouched: GetPlanByIdQuery enforces Plans.Read and the tenant query filter,
        // so an id from another tenant comes back empty and takes the SAME refusal as a plan that never
        // existed. The id buys precision, never access.
        if (planId is { } id)
        {
            try
            {
                var byId = await sender.Send(new GetPlanByIdQuery(id), cancellationToken);

                if (!byId.IsSuccess || byId.Value is null)
                {
                    // A stale or foreign id is a BUSINESS result — the plan is gone or was never
                    // theirs — so it refuses rather than raising. Unlike the id-from-our-own-list case
                    // below, this id came from the model's memory and may legitimately be wrong.
                    LogCause(AssistantToolCause.NotFound);
                    return Refusal();
                }

                // The other versions of the same plan, so the answer keeps the shape the name path
                // produces. Narrowed by the AUTHORITATIVE name from the row, never by the model's.
                var siblings = await ListVisiblePlansAsync(byId.Value.Name, cancellationToken);
                var otherVersions = siblings
                    .Where(p => p.Id != id && PlanNameMatch.AreSame(p.Name, byId.Value.Name))
                    .OrderByDescending(p => p.Version)
                    .Select(p => new PlanVersion(p.Version, p.Status))
                    .ToList();

                LogCause(AssistantToolCause.Found);
                return JsonSerializer.Serialize(
                    Describe(byId.Value, otherVersions, matchedExactly: true), Json);
            }
            catch (ForbiddenException)
            {
                LogCause(AssistantToolCause.NotPermitted);
                return Refusal();
            }
        }

        IReadOnlyList<PlanSummaryDto> visiblePlans;
        try
        {
            visiblePlans = await ListVisiblePlansAsync(planName, cancellationToken);
        }
        catch (ForbiddenException)
        {
            // The user may not read plans at all. Same refusal to the READER as "no such plan" — the
            // log is where the two are told apart.
            LogCause(AssistantToolCause.NotPermitted);
            return Refusal();
        }

        // ── The user named no plan ────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(planName))
        {
            if (visiblePlans.Count == 0)
            {
                LogCause(AssistantToolCause.NotFound);
                return Refusal();
            }

            if (visiblePlans.Count > 1)
            {
                // ★ NOT A REFUSAL, and the distinction matters. "Which plan?" is a question the model
                // must ask, not a "not found" it must report — reporting the second would tell a user
                // with four plans that they have none. The names are all inside the user's own tenant
                // and behind Plans.Read; nothing is disclosed that the plans screen does not show.
                logger.LogInformation("{Tool} finished: plan name required.", ToolName);
                return JsonSerializer.Serialize(
                    new PlanNameRequired(
                        Outcome: nameof(PlanRulesOutcome.PlanNameRequired),
                        Found: false,
                        Message: "The user did not say which plan. Ask them to pick one of these.",
                        AvailablePlans: visiblePlans
                            .Select(p => new PlanChoice(p.Name, p.Version, p.Status))
                            .ToList()),
                    Json);
            }
        }

        var matched = ResolveExactMatch(planName, visiblePlans);

        if (matched is null)
        {
            // ── The name did not match exactly ────────────────────────────────
            // ★ AND "NO SUCH PLAN" WAS A LIE. The model drops words out of a title it is retyping — it
            // sent "Plan Comercial EMEA (Test Integral)" for a plan stored as "Q3 2026 — Plan Comercial
            // EMEA (Test Integral)" in three of eight measured generations. The user was looking at the
            // plan on screen while being told it did not exist.
            var partial = visiblePlans
                .Where(p => PlanNameMatch.IsPartialNameOf(p.Name, planName))
                .ToList();

            if (partial.Count == 0)
            {
                // Covers "no such plan" and "another tenant's plan" alike: the query filter made the
                // second indistinguishable from the first before this line was reached. UNCHANGED, and
                // deliberately so — this is the path Rule 3 rests on, and nothing new is said here.
                LogCause(AssistantToolCause.NotFound);
                return Refusal();
            }

            // Candidates are counted by NAME, not by row. A plan cloned into a second version is two
            // rows sharing one name — one plan, and asking the user to choose between two identical
            // titles would be a question with no answer.
            var candidateNames = partial
                .Select(p => p.Name)
                .Distinct(PlanNameComparer)
                .ToList();

            if (candidateNames.Count > 1)
            {
                // Several DIFFERENT plans could be meant, so NONE of them is answered about. The
                // candidates are all inside the user's own tenant and behind Plans.Read — the same
                // disclosure the plans screen makes — and the assistant asks which one.
                logger.LogInformation("{Tool} finished: ambiguous partial name.", ToolName);
                return JsonSerializer.Serialize(
                    new PlanNameRequired(
                        Outcome: nameof(PlanRulesOutcome.PlanNameRequired),
                        Found: false,
                        Message: "No plan has exactly that name, and more than one could be meant. Ask "
                                 + "the user which of these they mean.",
                        AvailablePlans: candidateNames
                            .Select(name => partial.First(p => PlanNameMatch.AreSame(p.Name, name)))
                            .Select(p => new PlanChoice(p.Name, p.Version, p.Status))
                            .ToList()),
                    Json);
            }

            // ★ EXACTLY ONE PLAN COULD BE MEANT, SO THERE IS NO WRONG PLAN TO ANSWER ABOUT — and the
            // payload SAYS the name was not exact, so the user is never quietly handed a different
            // plan's rates. That disclosure is what makes resolving safe; without it this would be a
            // guess wearing a fact's clothes.
            //
            // Re-resolved against the FULL visible list, not against `partial`, so the version rules
            // (active first, then highest) see every version of the plan.
            matched = ResolveExactMatch(candidateNames[0], visiblePlans)! with { MatchedExactly = false };
            logger.LogInformation("{Tool} resolved a partial plan name to a single candidate.", ToolName);
        }

        // ── The rules themselves ──────────────────────────────────────────────
        // GetPlanByIdQuery is the only path that returns rules, and it returns ACTIVE rules only —
        // deactivated ones are not what anybody is paid under, so they are not what the assistant
        // describes.
        var detail = await sender.Send(new GetPlanByIdQuery(matched.Plan.Id), cancellationToken);

        if (!detail.IsSuccess || detail.Value is null)
        {
            // ★ A FAILED QUERY IS NOT AN EMPTY ONE. The id came from the list two lines up, so a failure
            // here is a fault (or a plan deleted mid-question), never "you have no such plan". Raised so
            // the runner fails the turn and the user sees a retry card — the lesson of the stochastic
            // bug, which reached the user as a confident denial of their own data.
            logger.LogError(
                "{Tool}: the plan query failed for a plan that was just listed ({Error}).",
                ToolName, detail.Error);
            LogCause(AssistantToolCause.QueryFailed);
            throw new InvalidOperationException($"The plan query failed: {detail.Error}");
        }

        LogCause(AssistantToolCause.Found);

        return JsonSerializer.Serialize(
            Describe(detail.Value, matched.OtherVersions, matched.MatchedExactly), Json);
    }

    // ── Resolution ────────────────────────────────────────────────────────────

    /// <summary>
    /// Every plan the asking user can see, narrowed by the name when there is one.
    ///
    /// The <c>Search</c> filter is a SUBSTRING match, which is why it is only ever a narrowing step:
    /// asking for "EMEA" would also return "EMEA Overlay", and answering a question about one plan with
    /// another plan's rates is precisely the quiet kind of wrong this product cannot afford. The exact
    /// match happens in <see cref="ResolveExactMatch"/>.
    ///
    /// ★ AND THE NARROWING KEY IS NOT THE NAME. It used to be, and that is how a plan the assistant had
    /// just explained came back as "does not exist": the model rewrote the name with an em-dash, the SQL
    /// <c>Contains</c> found no row, and the candidate list was empty before any matching happened.
    /// <see cref="PlanNameMatch.NarrowingKey"/> hands the database the part of the name that typographic
    /// substitution cannot touch. See that method for why this does not turn the lookup into a substring
    /// search, and for why no index is affected.
    /// </summary>
    private async Task<IReadOnlyList<PlanSummaryDto>> ListVisiblePlansAsync(
        string? planName, CancellationToken cancellationToken)
    {
        var listed = await sender.Send(
            new ListPlansQuery(new PaginationQuery
            {
                Page = 1,
                PageSize = MaxPlansListed,
                Search = PlanNameMatch.NarrowingKey(planName),
                SortBy = "name",
            }),
            cancellationToken);

        if (!listed.IsSuccess)
        {
            logger.LogError("{Tool}: the plan list query failed ({Error}).", ToolName, listed.Error);
            LogCause(AssistantToolCause.QueryFailed);
            throw new InvalidOperationException($"The plan list query failed: {listed.Error}");
        }

        return listed.Value?.Items ?? [];
    }

    /// <summary>
    /// The one plan to describe, and the other versions of it that exist.
    ///
    /// ★ EXACT, AFTER NORMALISATION — see <see cref="PlanNameMatch"/>. Still exact: the WHOLE name must
    /// match once both sides are folded, so "Q3_Enterprise" and "Q3_SMB" stay different and "Q3" alone
    /// finds nothing. What it no longer does is refuse a plan because a model typed an em-dash — the
    /// lesson of get_transaction's substring bug held (this never became a substring), but the opposite
    /// failure was waiting on the other side of it.
    /// ★ AND VERSIONED: plans are cloned into new versions under the SAME name,
    /// so a name legitimately resolves to several rows. Active wins (it is the one in force), then the
    /// highest version; the versions NOT chosen are reported alongside so the assistant can say which
    /// one it is describing instead of silently picking.
    /// </summary>
    private static PlanMatch? ResolveExactMatch(string? planName, IReadOnlyList<PlanSummaryDto> plans)
    {
        var candidates = string.IsNullOrWhiteSpace(planName)
            ? plans.ToList()
            : plans.Where(p => PlanNameMatch.AreSame(p.Name, planName)).ToList();

        if (candidates.Count == 0) return null;

        var chosen = candidates
            .OrderByDescending(p => string.Equals(p.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(p => p.Version)
            .First();

        var others = candidates
            .Where(p => p.Id != chosen.Id)
            .OrderByDescending(p => p.Version)
            .Select(p => new PlanVersion(p.Version, p.Status))
            .ToList();

        return new PlanMatch(chosen, others);
    }

    /// <summary>
    /// The plan id, when the model copied one out of the conversation. An unparseable value degrades to
    /// null so the name path still runs — a malformed id must not cost the user their answer.
    /// </summary>
    private Guid? ReadPlanId(string argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            return document.RootElement.TryGetProperty("planId", out var value)
                   && Guid.TryParse(value.GetString(), out var parsed)
                ? parsed
                : null;
        }
        catch (JsonException)
        {
            return null; // Already logged by ReadPlanName, which parses the same document.
        }
    }

    private string? ReadPlanName(string argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            return document.RootElement.TryGetProperty("planName", out var value)
                ? value.GetString()?.Trim()
                : null;
        }
        catch (JsonException)
        {
            // ★ NOT A REFUSAL HERE, unlike get_transaction. There, unreadable arguments meant no
            // reference and therefore nothing to look up. Here the argument is OPTIONAL: unreadable
            // arguments are the same situation as no plan named, and that has a useful answer.
            logger.LogWarning("The assistant produced unreadable arguments for {Tool}.", ToolName);
            return null;
        }
    }

    /// <summary>Says in the LOG what the user is deliberately not told. The plan NAME is not logged.</summary>
    private void LogCause(AssistantToolCause cause) =>
        logger.LogInformation("{Tool} finished: {Cause}.", ToolName, cause);

    private static string Refusal() =>
        JsonSerializer.Serialize(
            new RefusalPayload(nameof(PlanRulesOutcome.NotFoundOrNotVisible), false, RefusalMessage),
            Json);

    // ── Mapping the configuration to tokens ───────────────────────────────────

    private static PlanRules Describe(
        PlanDto plan, IReadOnlyList<PlanVersion> otherVersions, bool matchedExactly) =>
        new(
            Outcome: nameof(PlanRulesOutcome.PlanRules),
            Found: true,
            // ★ HOW THE NAME WAS RESOLVED, always stated. When the model's name was not the stored
            // one, the user must be told which plan they are actually being shown — otherwise a
            // near-miss reads as a confirmation of what they asked for.
            MatchedBy: matchedExactly
                ? nameof(NameMatchToken.ExactName)
                : nameof(NameMatchToken.PartialNameSingleCandidate),
            // ★ THE ID TRAVELS SO THE NEXT TURN DOES NOT HAVE TO REMEMBER THE TITLE. It lands in the
            // transcript the model re-reads, and "that plan" two questions later becomes a copied
            // identifier instead of a retyped name. Not PII: a plan is configuration, and its name —
            // which a person chose — was already here.
            PlanId: plan.Id,
            PlanName: plan.Name,
            PlanVersion: plan.Version,
            PlanStatus: plan.Status,
            CurrencyCode: plan.Currency,
            EffectiveFrom: plan.EffectiveStart.ToString("yyyy-MM-dd"),
            EffectiveTo: plan.EffectiveEnd.ToString("yyyy-MM-dd"),
            OtherVersionsOfThisPlan: otherVersions.Count == 0 ? null : otherVersions,
            // ★ THE ORDER IS DATA, because the assistant got it wrong by inference: asked to work
            // through a capped, accelerated rate it applied the accelerator and forgot the cap. The
            // engine's order is fixed (CreditAllocationService: rate → modifier → cap → floor) and
            // stating it removes the step the model was improvising.
            CalculationOrder: ["RateTable", "Modifier", "Cap", "Floor"],
            Clawback: plan.ClawbackMaturationDays is null && plan.ClawbackCapPercent is null
                ? null
                : new ClawbackPolicy(plan.ClawbackMaturationDays, plan.ClawbackCapPercent),
            Rules: plan.Rules.Select(r => PlanRuleProjection.DescribeRule(r, plan.Currency)).ToList());

    // ── The payload the model reads ───────────────────────────────────────────
    //
    // ★ NOTHING HERE NAMES A PERSON. No payee, no assignment, no id of anybody. A plan's configuration
    // is not personal data and this payload keeps it that way.

    /// <summary>The three shapes this tool can return, named so the model can branch on one field.</summary>
    private enum PlanRulesOutcome { PlanRules, PlanNameRequired, NotFoundOrNotVisible }

    /// <summary>How the requested name was resolved to a plan. Always present on a PlanRules answer.</summary>
    private enum NameMatchToken { ExactName, PartialNameSingleCandidate }

    private sealed record PlanMatch(
        PlanSummaryDto Plan,
        IReadOnlyList<PlanVersion> OtherVersions,
        bool MatchedExactly = true);

    /// <summary>
    /// Names compared the way <see cref="PlanNameMatch"/> compares them, so "two candidates" counts
    /// PLANS and not rows — a plan cloned into a second version is two rows sharing one name, and
    /// offering the user a choice between two identical titles is a question with no answer.
    /// </summary>
    private sealed class NormalisedNameComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => PlanNameMatch.AreSame(x, y);

        public int GetHashCode(string obj) =>
            PlanNameMatch.Normalize(obj).ToLowerInvariant().GetHashCode();
    }

    private sealed record PlanVersion(int Version, string Status);

    private sealed record PlanChoice(string Name, int Version, string Status);

    private sealed record RefusalPayload(string Outcome, bool Found, string Message);

    private sealed record PlanNameRequired(
        string Outcome, bool Found, string Message, IReadOnlyList<PlanChoice> AvailablePlans);

    private sealed record PlanRules(
        string Outcome,
        bool Found,
        string MatchedBy,
        Guid PlanId,
        string PlanName,
        int PlanVersion,
        string PlanStatus,
        string CurrencyCode,
        string EffectiveFrom,
        string EffectiveTo,
        IReadOnlyList<PlanVersion>? OtherVersionsOfThisPlan,
        IReadOnlyList<string> CalculationOrder,
        ClawbackPolicy? Clawback,
        IReadOnlyList<PlanRuleProjection.PlanRule> Rules);

    private sealed record ClawbackPolicy(int? MaturationDays, decimal? CapPercent);

}
