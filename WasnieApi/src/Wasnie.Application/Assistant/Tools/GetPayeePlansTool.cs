using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Assignments;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Application.Compensation.Queries.Payees;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Tools;

/// <summary>
/// The assistant's fourth read-only tool: WHICH PLANS IS THIS PERSON ON — their assignments.
///
/// ★ WHY IT EXISTS, AND IT IS A CATEGORY ERROR THAT IT DID NOT. "What plans does Ana have?" had no tool,
/// so the dispatcher routed it to the only tool with "plan" in its name: <c>get_plan_rules</c>, with the
/// PAYEE'S NAME as <c>planName</c>. No plan is called "Ana García", so the lookup refused, and the user
/// — one turn after being shown Ana's balance — was told Ana could not be found. The reproduced bug.
///
/// The two questions are not variants of each other. <c>get_plan_rules</c> answers HOW A PLAN PAYS: rate
/// tables, modifiers, caps. This answers WHICH PLANS A PERSON IS ON: the assignment, its period, its
/// status. One is configuration, the other is a relationship, and collapsing them is how a question about
/// a person gets answered with a rate table — or not at all.
///
/// ★ SINGLE RESPONSIBILITY, ENFORCED BY OMISSION. It reports no rate, no rule, no commission and no
/// balance. Asked "what does Ana earn on that plan", the model has to call the tool that answers that.
/// A payload that grew to cover the follow-up would be this tool becoming the two it sits between.
///
/// ★ RULE 1 — THROUGH THE DOMAIN, WITH THE ASKING USER'S IDENTITY. <c>ListPayeesQuery</c> (via
/// <see cref="PayeeResolver"/>) to turn a name into an id, then <c>ListAssignmentsByPayeeQuery</c>. No
/// DbContext in this file and there must never be one.
///
/// ★ RULE 2 — ISOLATION. <c>PayeeAccessGuard</c> runs inside <c>ListAssignmentsByPayeeHandler</c>, on
/// whatever id arrives, exactly as it does for the payee screen. An id reused from the conversation's
/// resolved-entity context gets the same check as one typed by hand: the context buys precision, never
/// access. This file does not know the visibility rules and must not learn them.
///
/// ★ RULE 3 — A REFUSAL SAYS NOTHING. "No such payee", "not yours" and "you may not read assignments"
/// return one identical payload. A TECHNICAL fault is raised instead, so the runner fails the turn and
/// the user gets a retry card rather than a sentence claiming their colleague does not exist.
/// </summary>
public sealed class GetPayeePlansTool(ISender sender, ILogger<GetPayeePlansTool> logger) : IAssistantTool
{
    public const string ToolName = "get_payee_plans";

    /// <summary>★ ONE SENTENCE, EVERY REFUSAL. Not two constants that happen to match today.</summary>
    public const string RefusalMessage =
        "No payee with that name was found, or you do not have access to their plan assignments.";

    /// <summary>
    /// How many assignments are returned. A payee on more than this many plans at once does not exist in
    /// practice; the cap is a backstop against one question becoming an unbounded page, not a sample.
    /// </summary>
    private const int MaxAssignments = 50;

    public AssistantToolSchema Schema { get; } = new(
        ToolName,
        "Read which compensation plans a payee is assigned to: the plan, the period the assignment "
        + "covers, and whether it is still active. Read-only, and it reports the ASSIGNMENT only — no "
        + "rates, no rules, no amounts. Use it whenever the user asks what plans someone is on, which "
        + "plan a person is assigned to, since when, or whether somebody has a plan at all. Do NOT use "
        + "get_plan_rules for this: that tool reads a plan's configuration and cannot find a person.",
        // Not strict, for the same reason as the other three tools: provider-side validation turns a
        // recoverable refusal into a 400 for the entire request.
        """
        {
          "type": "object",
          "properties": {
            "payeeId": {
              "type": "string",
              "description": "The payee's id, copied EXACTLY from the resolved-entity context of this conversation. Always prefer this over payeeName when the payee has already been looked up in this session — by ANY tool, including the balance one. It is exact, and retyping a name from memory is how the wrong person or a 'not found' happens."
            },
            "payeeName": {
              "type": "string",
              "description": "The payee's full name or employee code, copied verbatim from the user's message. Use ONLY for a payee that has not been looked up earlier in this conversation."
            },
            "includeEnded": {
              "type": "boolean",
              "description": "False (the default) returns only assignments that are currently active. True also returns deactivated ones. Use true only when the user asked about past or former plans."
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
        // The payload's only destination is the prompt — never HTML, never a URL. Same reasoning as the
        // other tools: a Spanish or Polish name must not reach the model as a wall of escape sequences.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<string> RunAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        var (payeeId, payeeName, includeEnded) = ReadArguments(argumentsJson);

        if (payeeId is null && string.IsNullOrWhiteSpace(payeeName))
        {
            LogCause(AssistantToolCause.UnreadableArguments);
            return Refusal();
        }

        // ── The id path: no resolution at all ────────────────────────────────
        //
        // ★ THIS IS THE BRANCH THE WHOLE WORK ITEM EXISTS FOR. The user asked about Ana's balance last
        // turn and now asks "and what plans does she have?" — a sentence naming nobody. The dispatcher
        // reads Ana's id out of the resolved-entity context and passes it here, and no name is retyped,
        // normalised or matched. Authorisation is untouched: the guard inside the query runs on this id
        // exactly as it would on one resolved from a name.
        if (payeeId is { } id)
        {
            // ★★ THE ID IS CONFIRMED BEFORE ANYTHING IS ASSERTED ABOUT IT — and the first version of
            // this tool did not, which was a real defect caught by the isolation test.
            //
            // Skipping straight to the assignments looks harmless because the guard still runs there.
            // It is not: that query answers a denial with an EMPTY PAGE, so a foreign tenant's id came
            // back as zero rows, and zero rows is the branch that reports `found: true` with the
            // "no assignments visible" outcome. The tool was therefore CONFIRMING THE EXISTENCE of a
            // payee it had never looked up, for any GUID at all — the precise fact Rule 3 exists to
            // withhold, reached without resolving anything.
            //
            // GetPayeeByIdQuery enforces Payees.Read and the tenant query filter, so a stale, foreign or
            // invented id comes back empty here and takes the SAME refusal as a payee who never existed.
            // It also hands back the canonical NAME, which the assignment rows cannot supply when there
            // are none — so the honest empty answer can still say who it is about.
            PayeeDto? confirmed;

            try
            {
                var byId = await sender.Send(new GetPayeeByIdQuery(id), cancellationToken);
                confirmed = byId.IsSuccess ? byId.Value : null;
            }
            catch (ForbiddenException)
            {
                LogCause(AssistantToolCause.NotPermitted);
                return Refusal();
            }

            if (confirmed is null)
            {
                LogCause(AssistantToolCause.NotFound);
                return Refusal();
            }

            return await DescribeAsync(
                id, confirmed.FullName, PayeeMatch.ResolvedById, includeEnded, cancellationToken);
        }

        // ── Name → id ────────────────────────────────────────────────────────
        PayeeResolution resolution;

        try
        {
            resolution = await PayeeResolver.ResolveAsync(sender, payeeName!, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // ★ A FAILED QUERY IS NOT AN EMPTY ONE. Raised, so the runner fails the turn and the user
            // gets a retry card instead of being told their colleague does not exist.
            logger.LogError("{Tool}: the payee lookup failed ({Error}).", ToolName, ex.Message);
            throw;
        }
        catch (ForbiddenException)
        {
            // Cannot even list payees. Same refusal to the reader; the log knows better.
            LogCause(AssistantToolCause.NotPermitted);
            return Refusal();
        }

        // ★ The same ambiguity, the same payload, the same rule in the prompt — see the balance tool and
        // PayeeAmbiguity. Two people called Anna Schmidt are two people whichever question is asked
        // about them, and the two tools must not disagree about that on adjacent turns.
        if (resolution.Match == PayeeMatch.Ambiguous)
        {
            LogCause(AssistantToolCause.AmbiguousPayee);
            return PayeeAmbiguity.Payload(payeeName!, resolution.Candidates);
        }

        if (resolution.Payee is null)
        {
            LogCause(AssistantToolCause.NotFound);
            return Refusal();
        }

        return await DescribeAsync(
            resolution.Payee.Id, resolution.Payee.FullName, resolution.Match, includeEnded,
            cancellationToken);
    }

    /// <summary>
    /// The half that is identical whichever way the payee was identified: ask the domain, shape the
    /// answer. Both callers reach the SAME guard and the SAME refusal.
    /// </summary>
    /// <param name="knownName">
    /// The payee's canonical name, ALWAYS present: both callers confirm the payee before arriving here,
    /// one through the name resolver and one through <c>GetPayeeByIdQuery</c>. That is what makes the
    /// empty answer below able to name the person it is about.
    /// </param>
    private async Task<string> DescribeAsync(
        Guid payeeId,
        string knownName,
        PayeeMatch match,
        bool includeEnded,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<PlanAssignmentDto>> assignments;

        try
        {
            assignments = await sender.Send(
                new ListAssignmentsByPayeeQuery(payeeId, new PaginationQuery
                {
                    Page = 1,
                    PageSize = MaxAssignments,
                    // The handler's contract: "all" is the explicit opt-in to every status, and anything
                    // else falls back to Active. No date filter is sent, so the full history of the
                    // chosen statuses is returned rather than a silent this-month window.
                    Status = includeEnded ? ListAssignmentsByPayeeHandler.AllStatuses : "Active",
                    SortOrder = "desc",
                }),
                cancellationToken);
        }
        catch (ForbiddenException)
        {
            // The caller got as far as listing payees but does not hold Assignments.Read.
            LogCause(AssistantToolCause.NotPermitted);
            return Refusal();
        }

        if (!assignments.IsSuccess || assignments.Value is null)
        {
            logger.LogError(
                "{Tool}: the assignment query failed ({Error}).", ToolName, assignments.Error);
            LogCause(AssistantToolCause.QueryFailed);
            throw new InvalidOperationException($"The assignment query failed: {assignments.Error}");
        }

        var rows = assignments.Value.Items ?? [];

        // ★★ AN EMPTY LIST IS TWO DIFFERENT FACTS AND THE TOOL CANNOT TELL THEM APART — so it must not
        // pick one. ListAssignmentsByPayeeHandler answers a PayeeAccessGuard denial with an EMPTY PAGE
        // rather than a Forbidden (deliberately: an error would confirm the payee exists). So zero rows
        // means EITHER this payee genuinely has no assignments OR this user may not see them, and
        // reporting "Ana is not on any plan" would be a confident false statement about somebody's
        // compensation in the second case.
        //
        // The outcome token says exactly that much and no more, and rule 22b turns it into a sentence
        // that is true under both readings. This is Rule 3 arriving through a side door: the refusal
        // stays indistinguishable, and the honest phrasing is the one that does not resolve it.
        if (rows.Count == 0)
        {
            LogCause(AssistantToolCause.NotFound);

            return JsonSerializer.Serialize(
                new NoAssignments(
                    Outcome: nameof(PayeePlansOutcome.NoAssignmentsOrNotVisible),
                    Found: true,
                    PayeeId: payeeId,
                    PayeeName: knownName,
                    MatchedBy: match.ToString(),
                    IncludedEnded: includeEnded,
                    Message: includeEnded
                        ? "No plan assignment of any status came back for this payee. Either they have "
                          + "never been assigned to a plan, or this user cannot see their assignments — "
                          + "you cannot tell which, so do not assert either."
                        : "No ACTIVE plan assignment came back for this payee. Either they have none "
                          + "right now, or this user cannot see their assignments — you cannot tell "
                          + "which, so do not assert either. They may still have ended assignments: "
                          + "offer to look again including past ones."),
                Json);
        }

        LogCause(AssistantToolCause.Found);

        // ★ THE AUTHORITATIVE NAME COMES FROM THE ROW, not from the caller. On the id path there is no
        // name to start from, and on the name path the row is still the record of truth — the resolver
        // may have matched an employee code or a partial name, in which case what the user typed is not
        // what the payee is called. Rule 19a's failure (a real payee reported as non-existent because
        // the name came back different) is the reason this is stated rather than assumed.
        var payeeName = rows[0].PayeeFullName;

        return JsonSerializer.Serialize(
            new PayeePlans(
                Outcome: nameof(PayeePlansOutcome.PayeePlans),
                Found: true,
                // ★ FIRST FIELD, AND IT IS THE CONVERSATION'S MEMORY. It is what ResolvedEntityContext
                // harvests off this turn, so the NEXT question about this person — their balance, their
                // rules — reuses the id instead of retyping the name. The cross-tool continuity this
                // work item is named for is this one line plus the channel that reads it.
                PayeeId: payeeId,
                PayeeName: payeeName,
                PayeeEmployeeCode: rows[0].PayeeEmployeeCode,
                MatchedBy: match.ToString(),
                IncludedEnded: includeEnded,
                AssignmentCount: rows.Count,
                // The domain's own count, so a payee on more plans than the cap is reported as truncated
                // rather than silently shortened into a complete-looking list.
                TotalAssignments: assignments.Value.TotalCount,
                Assignments: rows.Select(a => new PlanAssignment(
                    // ★ THE PLAN'S ID TRAVELS TOO. "And how does that second plan pay?" is the next
                    // question, and get_plan_rules takes a planId — so the follow-up is a copy rather
                    // than a retyped title, which is the failure that tool's whole history is made of.
                    PlanId: a.PlanId,
                    PlanName: a.PlanName,
                    PlanVersion: a.PlanVersion,
                    AssignmentStatus: a.Status,
                    EffectiveFrom: a.EffectiveStart.ToString("yyyy-MM-dd"),
                    EffectiveTo: a.EffectiveEnd.ToString("yyyy-MM-dd"),
                    Notes: a.Notes)).ToList()),
            Json);
    }

    private void LogCause(AssistantToolCause cause) =>
        logger.LogInformation("{Tool} finished: {Cause}.", ToolName, cause);

    /// <summary>The one refusal, serialised. Every cause reaches it; none is named.</summary>
    private static string Refusal() =>
        JsonSerializer.Serialize(
            new RefusalPayload(nameof(PayeePlansOutcome.NotFoundOrNotVisible), false, RefusalMessage),
            Json);

    private (Guid? PayeeId, string? PayeeName, bool IncludeEnded) ReadArguments(string argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            var root = document.RootElement;

            // An UNPARSEABLE id falls through to the name rather than refusing: the model occasionally
            // writes a placeholder, and the name it also sent is a perfectly good second chance. An id
            // that parses but belongs to nobody the caller may see is a different matter — that is the
            // guard's decision, not this method's.
            Guid? id = root.TryGetProperty("payeeId", out var idValue)
                       && Guid.TryParse(idValue.GetString(), out var parsed)
                ? parsed
                : null;

            var name = root.TryGetProperty("payeeName", out var nameValue)
                ? nameValue.GetString()?.Trim()
                : null;

            // ★ THE DEFAULT IS THE NARROW ANSWER. "What plans is Ana on" means now; answering with three
            // assignments that ended last year reads as three current plans, and the handler's own
            // contract makes the same choice for the same reason. A model that writes the string "true"
            // instead of the boolean is still understood — degrading to false on a typo is the
            // permissive-by-accident outcome this codebase keeps refusing.
            var includeEnded = root.TryGetProperty("includeEnded", out var endedValue)
                && endedValue.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.String => bool.TryParse(endedValue.GetString(), out var b) && b,
                    _ => false,
                };

            return (id, name, includeEnded);
        }
        catch (JsonException)
        {
            logger.LogWarning("The assistant produced unreadable arguments for {Tool}.", ToolName);
            return (null, null, false);
        }
    }

    // ── The payload the model reads ───────────────────────────────────────────

    /// <summary>The three shapes this tool can return, named so the model can branch on one field.</summary>
    private enum PayeePlansOutcome { PayeePlans, NoAssignmentsOrNotVisible, NotFoundOrNotVisible }

    /// <summary>
    /// ★ MINIMAL PII, AND NO MONEY AT ALL. A name and an employee code, because the answer is about a
    /// person and the reader has to know which one. No email, no manager, and — the point of the single
    /// responsibility — not one figure. An assignment says WHICH plan, not what it paid.
    /// </summary>
    private sealed record PayeePlans(
        string Outcome,
        bool Found,
        Guid PayeeId,
        string PayeeName,
        string? PayeeEmployeeCode,
        string MatchedBy,
        bool IncludedEnded,
        int AssignmentCount,
        int TotalAssignments,
        IReadOnlyList<PlanAssignment> Assignments);

    private sealed record PlanAssignment(
        Guid PlanId,
        string PlanName,
        int PlanVersion,
        string AssignmentStatus,
        string EffectiveFrom,
        string EffectiveTo,
        string? Notes);

    private sealed record NoAssignments(
        string Outcome,
        bool Found,
        Guid PayeeId,
        string PayeeName,
        string MatchedBy,
        bool IncludedEnded,
        string Message);

    private sealed record RefusalPayload(string Outcome, bool Found, string Message);
}
