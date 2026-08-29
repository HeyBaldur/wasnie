using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Credits;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Application.Compensation.Queries.Transactions;

namespace Wasnie.Application.Assistant.Tools;

/// <summary>
/// The assistant's first and only tool: look up ONE sale and what happened to it financially.
///
/// ★ RULE 1 — THROUGH THE DOMAIN, WITH THE ASKING USER'S IDENTITY, NEVER BY SHORTCUT. Every fact below
/// comes from the SAME MediatR query a screen sends. There is no DbContext in this file and there must
/// never be. The queries run inside the caller's request scope, so <c>ITenantContext</c> and
/// <c>ICurrentUserService</c> already hold the asking user — the tenant query filter and the
/// permission guards apply on their own, exactly as they do for the screen. Reaching for the database
/// "because it is one join" would mean re-implementing authorisation here, and getting it wrong the
/// first time somebody adds a role.
///
/// ★ RULE 3 — A REFUSAL SAYS NOTHING. "No such transaction" and "exists, but not yours" return the
/// IDENTICAL payload (<see cref="Refusal"/>). Distinguishing them would confirm the reference exists,
/// which is the fact an attacker is fishing for; the useful answer and the leak are the same sentence.
/// This is the pattern the chat's own conversation isolation already uses.
///
/// ★ READ-ONLY. Four queries, no commands. Nothing here writes, and the interface it implements has
/// nowhere to put a write.
/// </summary>
public sealed class GetTransactionTool(ISender sender, ILogger<GetTransactionTool> logger) : IAssistantTool
{
    public const string ToolName = "get_transaction";

    /// <summary>
    /// ★ ONE SENTENCE, IDENTICAL FOR BOTH REFUSALS. Not a template, not a formatted string with the
    /// reference in it, and deliberately not two constants that "happen" to read the same — a later
    /// edit to one of two strings is exactly how this leak comes back.
    /// </summary>
    public const string RefusalMessage =
        "No transaction with that reference was found, or you do not have access to it.";

    public AssistantToolSchema Schema { get; } = new(
        ToolName,
        "Look up one sales transaction by its reference number and report what happened to it: the "
        + "sale itself, the commission it generated, the plan rule that produced that commission, and "
        + "whether it has been paid out yet. Read-only. Use it whenever the user asks about a specific "
        + "transaction, deal or sale by reference.",
        // ★ DELIBERATELY NOT STRICT, and this is the fix for the 400. The schema used to carry
        // `"additionalProperties": false`, which asks the PROVIDER to validate the model's generated
        // call server-side — and when that validation fails the provider answers `400
        // tool_use_failed` for the WHOLE request. That is the worst place to be strict: a rejection
        // there is an HTTP failure we can only log, where the same rejection HERE is a clean refusal
        // the user can read. `ReadReference` already ignores anything it was not expecting, so the
        // strictness is not lost — it moved to the layer that can degrade gracefully.
        """
        {
          "type": "object",
          "properties": {
            "reference": {
              "type": "string",
              "description": "The transaction's reference number exactly as the user wrote it, for example TERM-CC-10."
            }
          },
          "required": ["reference"]
        }
        """);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>
    /// How many candidate payouts are opened to find the one that settled this credit.
    ///
    /// The candidates are already narrowed to one payee, one plan and the period containing the sale,
    /// which in practice is one payout and occasionally two (a supplemental run). The cap is a
    /// backstop against a pathological tenant turning one question into fifty queries, not a sampling
    /// strategy — when it bites, the answer says the settlement is undetermined rather than guessing.
    /// </summary>
    private const int MaxPayoutsInspected = 5;

    /// <summary>
    /// The label for a credit belonging to the payee the SALE is attributed to — the person the question
    /// is about.
    ///
    /// ★★ AN OPAQUE TOKEN, AND THAT IS THE POINT. It distinguishes rows without naming anybody, which is
    /// the only shape compatible with the pseudonymisation this product has already decided on
    /// (docs/Legal.md §3.2: the backend sends an opaque reference INSTEAD of the name). Adding
    /// payeeName here would fix the same bug and walk straight into an open GDPR finding, with no DPA
    /// signed — see docs/DIAG_ASSISTANT_CREDIT_PAYLOAD_OWNERSHIP.md for both options and the decision.
    ///
    /// ★ IT IS A TOKEN THE MODEL READS AND NEVER PRINTS, exactly like `matchedBy: ResolvedById` or
    /// `interpretation: EarningsAndNoDebt` elsewhere in these payloads. Prompt rule 10a already forbids
    /// putting any of them on the page.
    /// </summary>
    private const string TransactionPayeeRef = "TransactionPayee";

    /// <summary>
    /// Prefix for everybody else on the same sale — <c>OtherPayee1</c>, <c>OtherPayee2</c>… A reassigned
    /// transaction leaves the previous holder's superseded credit here, and a split leaves a colleague's.
    /// The number is a position in this one answer and means nothing outside it.
    /// </summary>
    private const string OtherPayeeRefPrefix = "OtherPayee";

    /// <summary>
    /// How <c>commissions</c> is sorted, said out loud. Newest allocation first — the order
    /// <see cref="ReadCommissionAsync"/> explicitly asks the credits query for.
    /// </summary>
    private const string OrderedByToken = "MostRecentlyAllocatedFirst";

    public async Task<string> RunAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        var reference = ReadReference(argumentsJson);

        if (string.IsNullOrWhiteSpace(reference))
        {
            LogCause(AssistantToolCause.UnreadableArguments);
            return Refusal();
        }

        // ── The sale itself ──────────────────────────────────────────────────
        // ReferenceNumbers is the EXACT-match filter (the `Reference` field is a substring search, which
        // would let "CC-1" answer with "TERM-CC-10"). Guarded by Transactions.Read and scoped by the
        // tenant query filter, both inside the handler.
        TransactionDto? transaction;
        try
        {
            var found = await sender.Send(
                new ListTransactionsQuery(new PaginationQuery
                {
                    Page = 1,
                    PageSize = 2,
                    ReferenceNumbers = reference,
                }),
                cancellationToken);

            // ★ A FAILED QUERY IS NOT AN EMPTY ONE. This used to fold `IsSuccess == false` into the
            // same null as "no rows matched", so a broken query came back to the user as "no such
            // transaction". Two different things wearing one answer — and the wrong one of the two is
            // a lie about their data. It is raised instead, and the runner turns it into a real error.
            if (!found.IsSuccess)
            {
                logger.LogError(
                    "{Tool}: the transaction query failed ({Error}).", ToolName, found.Error);
                throw new InvalidOperationException($"The transaction query failed: {found.Error}");
            }

            transaction = found.Value?.Items?.FirstOrDefault(t =>
                string.Equals(t.ReferenceNumber, reference, StringComparison.OrdinalIgnoreCase));
        }
        catch (ForbiddenException)
        {
            // ★ The user may not read transactions at all. Same refusal to the READER — see Rule 3 —
            // but the log now says which of the two it was.
            LogCause(AssistantToolCause.NotPermitted);
            return Refusal();
        }

        if (transaction is null)
        {
            // Covers both "no such reference" and "another tenant's reference": the query filter makes
            // the second one indistinguishable from the first before this line is even reached.
            LogCause(AssistantToolCause.NotFound);
            return Refusal();
        }

        LogCause(AssistantToolCause.Found);

        // ── What it earned ───────────────────────────────────────────────────
        // ★ THE SALE'S OWN PAYEE GOES IN. The credit rows are filtered by TRANSACTION, not by person,
        // so they can belong to more than one — and which of them is the payee the question is about is
        // the fact that decides whether an amount may be attributed to them at all.
        var commission = await ReadCommissionAsync(
            reference, transaction.PayeeId, cancellationToken);

        // ── Whether it has been paid ─────────────────────────────────────────
        var settlement = commission.Credits.Count == 0
            ? SettlementResult.NoCommission
            : await ReadSettlementAsync(transaction, commission, cancellationToken);

        var payload = new TransactionLifecycle(
            Found: true,
            Reference: transaction.ReferenceNumber,
            Description: transaction.Description,
            TransactionDate: transaction.TransactionDate.ToString("yyyy-MM-dd"),
            SaleAmount: transaction.Amount,
            SaleCurrency: transaction.Currency,
            Quantity: transaction.Quantity,
            TransactionStatus: transaction.Status,
            PayeeName: transaction.PayeeName,
            PayeeEmployeeCode: transaction.PayeeEmployeeCode,
            ProductName: transaction.ProductName,
            ProductCategory: transaction.Category,
            IngestedAt: transaction.IngestedAt.ToString("yyyy-MM-dd"),
            IngestedFrom: transaction.Source,
            CancelledAt: transaction.CancelledAt?.ToString("yyyy-MM-dd"),
            CancelledReason: transaction.CancelledReason,
            CommissionVisibleToYou: commission.Visible,
            CommissionGenerated: commission.Visible ? commission.Credits.Count > 0 : null,
            Commissions: commission.Visible ? commission.Credits : null,
            // ★ THE ORDER IS DATA, NOT SOMETHING TO INFER. Stated only when there is a list to order;
            // see the field's own note on TransactionLifecycle for what went wrong without it.
            CommissionsOrderedBy: commission.Visible && commission.Credits.Count > 1
                ? OrderedByToken
                : null,
            SettlementVisibleToYou: settlement.Visible,
            HasBeenPaid: settlement.HasBeenPaid,
            PayoutStatus: settlement.PayoutStatus,
            PayRunPeriodStart: settlement.PeriodStart,
            PayRunPeriodEnd: settlement.PeriodEnd,
            PayoutTotalCommissionAmount: settlement.PayoutTotalAmount,
            PayoutTotalCommissionCurrency: settlement.PayoutTotalCurrency,
            SettlementNote: settlement.Note);

        return JsonSerializer.Serialize(payload, Json);
    }

    /// <summary>
    /// Says in the LOG what the user is deliberately not told.
    ///
    /// ★ THE BLIND SPOT THIS CLOSES. Rule 3 makes "no such record" and "not yours" identical to the
    /// READER on purpose — telling them apart would confirm the record exists. The log inherited that
    /// blindness for free, and never should have: an operator could not tell a correct refusal from a
    /// broken lookup, so a real fault was invisible by design. That is what made the reported
    /// inconsistency impossible to diagnose from the logs.
    ///
    /// ★ THE REFERENCE IS NOT LOGGED. The cause is operational; the identifier is the user's own data,
    /// and the same reason the question itself is never logged applies here.
    /// </summary>
    private void LogCause(AssistantToolCause cause) =>
        logger.LogInformation("{Tool} finished: {Cause}.", ToolName, cause);

    /// <summary>The one refusal, serialised. Both causes reach it; neither is named.</summary>
    private static string Refusal() =>
        JsonSerializer.Serialize(new RefusalPayload(false, RefusalMessage), Json);

    private string? ReadReference(string argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            return document.RootElement.TryGetProperty("reference", out var value)
                ? value.GetString()?.Trim()
                : null;
        }
        catch (JsonException)
        {
            // The model wrote something that is not JSON. Nothing to look up, and nothing to say about
            // it beyond the ordinary refusal.
            logger.LogWarning("The assistant produced unreadable arguments for {Tool}.", ToolName);
            return null;
        }
    }

    /// <summary>
    /// The credits this sale produced, if the user is allowed to see credits at all.
    ///
    /// ★ A MISSING PERMISSION IS NOT A FAILED ANSWER. Credits.Read and Transactions.Read are separate
    /// permissions, and a role that has one without the other is an ordinary configuration, not an
    /// attack. So this degrades to "not visible to you" and the assistant answers about the sale —
    /// rather than refusing the whole question, which would tell the user their own transaction does
    /// not exist. The DOMAIN still decides; this only chooses what to do with its answer.
    /// </summary>
    private async Task<CommissionResult> ReadCommissionAsync(
        string reference, Guid? transactionPayeeId, CancellationToken cancellationToken)
    {
        try
        {
            var credits = await sender.Send(
                new ListCreditsQuery(new CreditFilterQuery
                {
                    Page = 1,
                    PageSize = 25,
                    Reference = reference,
                    Status = "All",
                    // ★ THE SORT IS ASKED FOR, NOT INHERITED. It happens to be ListCreditsHandler's
                    // default today, and the payload now DECLARES this order to the model — so relying
                    // on someone else's default would mean the declaration silently becomes a lie the
                    // day that default changes. Stating it here is what makes OrderedByToken true.
                    SortBy = "allocatedAt",
                    SortOrder = "desc",
                }),
                cancellationToken);

            if (!credits.IsSuccess || credits.Value?.Items is null)
            {
                return new CommissionResult(true, []);
            }

            // `Reference` is a SUBSTRING filter on the credit's transaction reference, so "TERM-CC-1"
            // would drag in "TERM-CC-10". Narrowed back to the exact reference here.
            var mine = credits.Value.Items
                .Where(c => string.Equals(c.ReferenceNumber, reference, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // ★★ WHOSE ROW IS THIS. Assigned once per distinct payee, in the order the rows arrive, so
            // two credits of one person share a label and two people never share one. See
            // CommissionEarned.PayeeRef for why the payload carries a token instead of a name.
            var refByPayee = new Dictionary<Guid, string>();
            var otherPayees = 0;

            string LabelFor(Guid payeeId)
            {
                if (refByPayee.TryGetValue(payeeId, out var existing)) return existing;

                var label = transactionPayeeId is not null && payeeId == transactionPayeeId
                    ? TransactionPayeeRef
                    : $"{OtherPayeeRefPrefix}{++otherPayees}";

                refByPayee[payeeId] = label;
                return label;
            }

            return new CommissionResult(
                true,
                mine.Select(c => new CommissionEarned(
                    CommissionAmount: c.CreditedAmount,
                    CommissionCurrency: c.CreditedCurrency,
                    MatchedPlanName: c.PlanName,
                    MatchedRuleName: c.RuleName,
                    CreditIsSuperseded: c.IsSuperseded,
                    CreditAllocatedAt: c.AllocatedAt.ToString("yyyy-MM-dd"),
                    PayeeRef: LabelFor(c.PayeeId),
                    CreditId: c.Id,
                    PayeeId: c.PayeeId,
                    PlanId: c.PlanId)).ToList());
        }
        catch (ForbiddenException)
        {
            return new CommissionResult(false, []);
        }
    }

    /// <summary>
    /// Whether the commission has been paid, found by walking from the credit to the payout LINE that
    /// consumed it.
    ///
    /// ★ WHY IT IS A WALK AND NOT ONE QUERY. No domain query answers "which payout settled this
    /// credit" — the link lives on the payout's lines. Rather than write a new query that reaches
    /// across the tables (and would have to re-derive the guards), the existing ones are composed: list
    /// the payouts of THIS payee, on THIS plan, whose period contains the sale, then open them and look
    /// for a line carrying this credit. Every step is guarded by Payouts.Read and the tenant filter,
    /// which is the whole point of composing rather than shortcutting.
    /// </summary>
    private async Task<SettlementResult> ReadSettlementAsync(
        TransactionDto transaction, CommissionResult commission, CancellationToken cancellationToken)
    {
        var creditIds = commission.Credits.Select(c => c.CreditId).ToHashSet();
        var first = commission.Credits[0];

        try
        {
            var payouts = await sender.Send(
                new ListPayoutsQuery(new PayoutFilterQuery
                {
                    Page = 1,
                    PageSize = MaxPayoutsInspected,
                    PayeeIds = first.PayeeId.ToString(),
                    PlanIds = first.PlanId.ToString(),
                    // A payout matches when its own period intersects the sale's date — the domain's
                    // own period semantics, not a guess about month boundaries.
                    PeriodFrom = transaction.TransactionDate,
                    PeriodTo = transaction.TransactionDate,
                }),
                cancellationToken);

            if (!payouts.IsSuccess || payouts.Value?.Items is null || payouts.Value.Items.Count == 0)
            {
                return SettlementResult.NotYetPaid;
            }

            foreach (var candidate in payouts.Value.Items.Take(MaxPayoutsInspected))
            {
                var detail = await sender.Send(new GetPayoutByIdQuery(candidate.Id), cancellationToken);

                if (!detail.IsSuccess || detail.Value is null)
                {
                    continue;
                }

                if (!detail.Value.Lines.Any(l => creditIds.Contains(l.CreditId)))
                {
                    continue;
                }

                return new SettlementResult(
                    Visible: true,
                    // Only "Paid" means the money left. Approved and Calculated are stages before that,
                    // and calling them paid would be the assistant telling somebody they have been paid
                    // when they have not.
                    HasBeenPaid: string.Equals(detail.Value.Status, "Paid", StringComparison.OrdinalIgnoreCase),
                    PayoutStatus: detail.Value.Status,
                    PeriodStart: detail.Value.PeriodStart.ToString("yyyy-MM-dd"),
                    PeriodEnd: detail.Value.PeriodEnd.ToString("yyyy-MM-dd"),
                    PayoutTotalAmount: detail.Value.TotalCommissionAmount,
                    PayoutTotalCurrency: detail.Value.TotalCommissionCurrency,
                    Note: null);
            }

            return SettlementResult.NotYetPaid;
        }
        catch (ForbiddenException)
        {
            return SettlementResult.NotVisible;
        }
    }

    // ── The payload the model reads ───────────────────────────────────────────

    /// <summary>
    /// ★ THE NAMES ARE THE INTERFACE. A model reading `amount` next to `total` has to guess which is
    /// the sale and which is the commission, and a guess about that is a number in front of a user who
    /// believes it. `saleAmount` and `commissionAmount` cannot be confused; `hasBeenPaid` cannot be
    /// read as "approved". Every field here is named for what it MEANS, not for the column it came
    /// from — which is also why nulls are omitted rather than sent as empty strings a model might
    /// print.
    ///
    /// Ids are included only where they are needed to join the walk together; nothing here is a secret,
    /// but nothing here is a screen the assistant can link to either.
    /// </summary>
    private sealed record TransactionLifecycle(
        bool Found,
        string Reference,
        string? Description,
        string TransactionDate,
        decimal SaleAmount,
        string SaleCurrency,
        int Quantity,
        string TransactionStatus,
        string? PayeeName,
        string? PayeeEmployeeCode,
        string? ProductName,
        string? ProductCategory,
        string IngestedAt,
        string IngestedFrom,
        string? CancelledAt,
        string? CancelledReason,
        bool CommissionVisibleToYou,
        bool? CommissionGenerated,
        IReadOnlyList<CommissionEarned>? Commissions,
        /// <summary>
        /// ★ THE ORDER IS DATA. Null unless there is more than one credit, because a list of one has no
        /// order to state. The model was inferring meaning from position — "the first credit" — over a
        /// list whose order it had never been told, which is how a date from row 1 ended up beside an
        /// amount from row 0. Same cure `get_plan_rules` already uses for CalculationOrder.
        /// </summary>
        string? CommissionsOrderedBy,
        bool SettlementVisibleToYou,
        bool? HasBeenPaid,
        string? PayoutStatus,
        string? PayRunPeriodStart,
        string? PayRunPeriodEnd,
        decimal? PayoutTotalCommissionAmount,
        string? PayoutTotalCommissionCurrency,
        string? SettlementNote);

    /// <param name="PayeeRef">
    /// ★★ WHOSE CREDIT THIS IS — THE FIELD WHOSE ABSENCE PRODUCED A FALSE TABLE ABOUT MONEY.
    ///
    /// The credits are looked up BY TRANSACTION, so one sale reassigned from one person to another
    /// returns both rows. Without this field nothing in the payload distinguished them, and the
    /// assistant — asked about one payee, handed two people's credits — reported the two rows shuffled:
    /// the amounts in array order and the dates in reverse, with `superseded` on the wrong one. Every
    /// individual value was real; the pairing was invented. No prompt rule could have caught that,
    /// because the model was not given anything to tell the rows apart with
    /// (docs/DIAG_ASSISTANT_CREDIT_PAYLOAD_OWNERSHIP.md).
    ///
    /// <c>TransactionPayee</c> for the person this sale belongs to, <c>OtherPayee1..n</c> for anyone
    /// else. Rows of the same person share a label; two people never do.
    ///
    /// ★ NOT A NAME, DELIBERATELY. See <see cref="TransactionPayeeRef"/>.
    /// </param>
    private sealed record CommissionEarned(
        decimal CommissionAmount,
        string CommissionCurrency,
        string MatchedPlanName,
        string MatchedRuleName,
        bool CreditIsSuperseded,
        string CreditAllocatedAt,
        string PayeeRef,
        [property: JsonIgnore] Guid CreditId,
        [property: JsonIgnore] Guid PayeeId,
        [property: JsonIgnore] Guid PlanId);

    private sealed record RefusalPayload(bool Found, string Message);

    private sealed record CommissionResult(bool Visible, IReadOnlyList<CommissionEarned> Credits);

    private sealed record SettlementResult(
        bool Visible,
        bool? HasBeenPaid,
        string? PayoutStatus,
        string? PeriodStart,
        string? PeriodEnd,
        decimal? PayoutTotalAmount,
        string? PayoutTotalCurrency,
        string? Note)
    {
        public static readonly SettlementResult NoCommission = new(
            true, false, null, null, null, null, null,
            "This transaction has not produced any commission credit, so there is nothing to pay out.");

        public static readonly SettlementResult NotYetPaid = new(
            true, false, null, null, null, null, null,
            "The commission has been credited but does not yet appear in any payout.");

        public static readonly SettlementResult NotVisible = new(
            false, null, null, null, null, null, null, null);
    }
}
