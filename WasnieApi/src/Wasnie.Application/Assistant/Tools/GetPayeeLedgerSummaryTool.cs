using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Ledger;
using Wasnie.Application.Compensation.Queries.Payees;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Assistant.Tools;

/// <summary>
/// The assistant's third read-only tool: WHAT IS THIS PERSON'S BALANCE — earnings and debt, together.
///
/// ★★ IT EXISTS TO KILL THE FALSE ZERO. Asked "what is Ana's balance", an assistant with no tool either
/// gives up or reaches for the ledger, and the ledger records ONLY debt. A rep who earned 10,000 with no
/// clawback has a ledger balance of 0.00, so the honest-looking answer is "your balance is €0" — said to
/// a salesperson who is owed ten thousand. The crossing happens in the query
/// (<see cref="GetPayeeLedgerSummaryQuery"/>) and the classification arrives as a token, so this tool
/// never computes and the model never infers.
///
/// ★ RULE 1 — THROUGH THE DOMAIN, WITH THE ASKING USER'S IDENTITY. Two MediatR queries, no DbContext:
/// the payee list to turn a name into an id (the same query the payee screen sends), then the summary,
/// which enforces Ledger.Read + Payouts.Read AND the resource guard on its own. Authorisation is never
/// re-implemented here.
///
/// ★ RULE 2 — ISOLATION. Handled entirely by PayeeAccessGuard inside the query. This file does not know
/// the rules and must not learn them.
///
/// ★ RULE 3 — A REFUSAL SAYS NOTHING, A FAILURE SAYS EVERYTHING. "No such payee", "not yours" and "you
/// may not read balances" return one identical payload. A TECHNICAL fault is raised instead, so the
/// runner fails the turn and the user gets a retry card — never a sentence claiming their colleague
/// does not exist because a query broke.
///
/// ★ AGGREGATES ONLY. No individual payout, no ledger entry, no transaction. Single responsibility, and
/// a context window spent on rows is a context window not spent on the answer. Breaking down a payout
/// and investigating a clawback are separate tools, deliberately not this one.
/// </summary>
public sealed class GetPayeeLedgerSummaryTool(ISender sender, ILogger<GetPayeeLedgerSummaryTool> logger)
    : IAssistantTool
{
    public const string ToolName = "get_payee_balance";

    /// <summary>★ ONE SENTENCE, EVERY REFUSAL. Not two constants that happen to match today.</summary>
    public const string RefusalMessage =
        "No payee with that name was found, or you do not have access to their balance.";

    public AssistantToolSchema Schema { get; } = new(
        ToolName,
        "Read one payee's balance: the commission they have earned, what they still owe the company "
        + "from clawbacks, and the net amount they can expect to be paid. Aggregated totals only, never "
        + "individual transactions. Read-only. Use it whenever the user asks what someone's balance is, "
        + "how much is owed to or by a payee, or how much someone is going to be paid.",
        // Not strict, for the same reason as the other two tools: provider-side validation turns a
        // recoverable refusal into a 400 for the entire request.
        """
        {
          "type": "object",
          "properties": {
            "payeeId": {
              "type": "string",
              "description": "The payee's id, copied EXACTLY from an earlier answer in this conversation. Always prefer this over payeeName when the payee has already been looked up in this session — it is exact, and retyping a name from memory is how the wrong person or a 'not found' happens."
            },
            "payeeName": {
              "type": "string",
              "description": "The payee's full name or employee code, copied verbatim from the user's message. Use ONLY for a payee that has not been looked up earlier in this conversation."
            },
            "period": {
              "type": "string",
              "description": "Which window the earnings cover. One of: all-time, this-month, last-month, this-quarter, last-quarter, ytd, last-year. Use all-time unless the user asked about a specific period.",
              "enum": ["all-time", "this-month", "last-month", "this-quarter", "last-quarter", "ytd", "last-year"]
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
        // The payload's only destination is the prompt — never HTML, never a URL. Same reasoning, and
        // the same caveat, as GetPlanRulesTool: a Spanish or Polish name must not reach the model as a
        // wall of escape sequences.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<string> RunAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        var (payeeId, payeeName, period) = ReadArguments(argumentsJson);

        if (payeeId is null && string.IsNullOrWhiteSpace(payeeName))
        {
            LogCause(AssistantToolCause.UnreadableArguments);
            return Refusal();
        }

        // ── The id path: no resolution at all ────────────────────────────────
        //
        // ★ WHY THE ID IS ACCEPTED AT ALL, AND WHY IT IS NOT A SECURITY HOLE. A name is a guess the
        // model retypes; an id is a fact it copies. Nothing about authorisation changes: the summary
        // query runs PayeeAccessGuard on whatever id arrives, so an id lifted from anywhere — another
        // tenant, another conversation, a guess — gets the same indistinguishable refusal as a payee
        // that does not exist. The id buys precision, never access.
        //
        // ★ AND IT IS THE FIX FOR A REAL BROKEN CONVERSATION. Turn 1 explains Ana's balance; turn 2 asks
        // "and how much of that is the clawback?"; the model retypes the name from memory, drops a word
        // or substitutes a dash, and the tool answers "no payee with that name" about the person it just
        // described.
        //
        // ★ WHERE THE ID ACTUALLY COMES FROM — and for a long time the answer was NOWHERE. The schema
        // below says "copied from an earlier answer in this conversation", which was not achievable: the
        // component that writes these arguments is the dispatcher, it reads only message TEXT, and rule
        // 18 forbids an id ever appearing there. <see cref="ResolvedEntityContext"/> is the channel that
        // makes the sentence true — this turn's payeeId is stored on the turn and handed to the
        // dispatcher next time. Without it this branch was reachable only if the USER typed a GUID.
        PayeeResolution resolution;

        if (payeeId is { } id)
        {
            // No lookup: the guard inside the query is the check, and a name lookup here would only
            // reintroduce the failure the id exists to avoid.
            return await SummariseAsync(id, PayeeMatch.ResolvedById, period, cancellationToken);
        }

        // ── Name → id ────────────────────────────────────────────────────────
        try
        {
            resolution = await PayeeResolver.ResolveAsync(sender, payeeName!, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // ★ A FAILED QUERY IS NOT AN EMPTY ONE. Raised by the resolver and re-raised here, so the
            // runner fails the turn instead of the user being told their colleague does not exist.
            logger.LogError("{Tool}: the payee lookup failed ({Error}).", ToolName, ex.Message);
            throw;
        }
        catch (ForbiddenException)
        {
            // Cannot even list payees. Same refusal to the reader; the log knows better.
            LogCause(AssistantToolCause.NotPermitted);
            return Refusal();
        }

        // ★ SEVERAL PEOPLE, NOT NO PEOPLE — and until this branch existed the two were the same answer.
        // The resolver has always refused to choose between two payees sharing a name, correctly; the
        // refusal simply arrived as null and was relayed as "no payee with that name was found" about
        // somebody the user could see on their own screen. Nothing is read for anyone here: the user is
        // handed the candidates and asked which.
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

        return await SummariseAsync(
            resolution.Payee.Id, resolution.Match, period, cancellationToken);
    }

    /// <summary>
    /// The half that is identical whichever way the payee was identified: ask the domain, shape the
    /// answer. Both callers reach the SAME guard and the SAME refusal.
    /// </summary>
    private async Task<string> SummariseAsync(
        Guid payeeId, PayeeMatch match, string period, CancellationToken cancellationToken)
    {

        // ── The balance ──────────────────────────────────────────────────────
        Result<PayeeLedgerSummaryDto> summary;
        try
        {
            summary = await sender.Send(
                new GetPayeeLedgerSummaryQuery(payeeId, period), cancellationToken);
        }
        catch (ForbiddenException)
        {
            // The caller got as far as listing payees but does not hold LedgerSummary.Read.
            //
            // ★ ONE PERMISSION, NOT TWO, SINCE THE FACADE. This used to require Payouts.Read as well,
            // which blocked Rep and Manager outright — the roles the false zero hurts most. The fix was
            // NOT to hand them the payroll surface: LedgerSummary.Read authorises the finished figure
            // and nothing underneath it. Refusal here now means the seat genuinely has no balance
            // access, not that the permissions could only tell half the truth.
            LogCause(AssistantToolCause.NotPermitted);
            return Refusal();
        }

        if (!summary.IsSuccess || summary.Value is null)
        {
            // The query's own refusal (unknown payee, or one this user may not see) is a BUSINESS
            // result and shares the single refusal sentence. It is not raised: nothing is broken.
            LogCause(AssistantToolCause.NotFound);
            return Refusal();
        }

        LogCause(AssistantToolCause.Found);

        var value = summary.Value;

        return JsonSerializer.Serialize(new BalancePayload(
            Found: true,
            // ★ FIRST FIELD, AND IT IS THE CONVERSATION'S MEMORY. Everything the model needs to ask a
            // follow-up about this exact payee is now in the transcript it re-reads: it copies this id
            // instead of retyping a name it half-remembers. It is also what makes COMPARISONS work —
            // three payees looked up across three turns leave three ids in the history, where a
            // server-side "last entity" anchor would have kept only the newest.
            PayeeId: value.PayeeId,
            PayeeName: value.PayeeName,
            MatchedBy: match.ToString(),
            Period: value.PeriodLabel,
            PeriodStart: value.PeriodStart?.ToString("yyyy-MM-dd"),
            PeriodEnd: value.PeriodEnd?.ToString("yyyy-MM-dd"),
            // An empty list is a real answer — the payee exists and has no money on either side — and
            // it must not be confused with the refusal above.
            Balances: value.ByCurrency.Select(c => new CurrencyBalance(
                Currency: c.Currency,
                EarnedCommissions: c.EarnedCommissionsInPeriod,
                AlreadyPaidOut: c.PaidOutInPeriod,
                Disputed: c.DisputedInPeriod == 0m ? null : c.DisputedInPeriod,
                AwaitingPayment: c.AwaitingPaymentAllTime,
                OutstandingDebt: c.OutstandingDebt,
                NetPendingPayout: c.NetPendingPayout,
                Interpretation: c.Interpretation.ToString())).ToList()), Json);
    }

    private void LogCause(AssistantToolCause cause) =>
        logger.LogInformation("{Tool} finished: {Cause}.", ToolName, cause);

    /// <summary>The one refusal, serialised. Every cause reaches it; none is named.</summary>
    private static string Refusal() =>
        JsonSerializer.Serialize(new RefusalPayload(false, RefusalMessage), Json);

    private (Guid? PayeeId, string? PayeeName, string Period) ReadArguments(string argumentsJson)
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

            // An unrecognised period degrades to all-time inside PeriodHelper, so nothing is validated
            // here — a model writing "Q3" must still get an answer, just a wider one.
            var period = root.TryGetProperty("period", out var periodValue)
                ? periodValue.GetString()?.Trim() ?? "all-time"
                : "all-time";

            return (id, name, string.IsNullOrWhiteSpace(period) ? "all-time" : period);
        }
        catch (JsonException)
        {
            logger.LogWarning("The assistant produced unreadable arguments for {Tool}.", ToolName);
            return (null, null, "all-time");
        }
    }

    // ── The payload the model reads ───────────────────────────────────────────

    /// <summary>
    /// ★ THE FIELD NAMES ARE THE INTERFACE. `outstandingDebt` cannot be read as money coming, and
    /// `netPendingPayout` cannot be read as a debt. The one field that decides the story is
    /// `interpretation`, a token the system prompt teaches — the model renders it, it never derives it.
    ///
    /// ★ MINIMAL PII. One name, because the answer is about a person and the reader has to know which
    /// one; no email, no employee code, no id, no manager. The payee id in particular is deliberately
    /// absent: it is of no use in a sentence and every identifier that travels is one more thing to
    /// leak.
    /// </summary>
    private sealed record BalancePayload(
        bool Found,
        Guid PayeeId,
        string PayeeName,
        string MatchedBy,
        string Period,
        string? PeriodStart,
        string? PeriodEnd,
        IReadOnlyList<CurrencyBalance> Balances);

    private sealed record CurrencyBalance(
        string Currency,
        decimal EarnedCommissions,
        decimal AlreadyPaidOut,
        decimal? Disputed,
        decimal AwaitingPayment,
        decimal OutstandingDebt,
        decimal NetPendingPayout,
        string Interpretation);

    private sealed record RefusalPayload(bool Found, string Message);
}
