using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Plans;

namespace Wasnie.Application.Assistant.Tools;

/// <summary>
/// What each rule of a plan would pay for a hypothetical transaction — computed by the engine.
///
/// ★★ THE DEFECT THIS CLOSES. Asked what three rules would pay on a 7,850 sale with 5 units, the
/// assistant did the arithmetic in prose for rule 1 (7,850 × 0.05 × 1.2 = 471) and got it right;
/// said it could not do rule 2 "because the per-unit amount is missing" when the amount was €5.00 and
/// was in the payload; and described rule 3's revenue brackets as "quota attainment 0 – 20,000 %".
/// One right answer by luck, one refusal for the wrong reason, one confident mis-description.
///
/// ★ AND THE RIGHT ANSWER WAS THE WORST OF THE THREE. Rule 10d forbids the model deriving figures the
/// lookup did not return, precisely because the day the cascade has a cap and a floor in it, prose
/// arithmetic stops being right and nothing announces the change. The problem was never that the
/// model was bad at multiplying — it was that it had nothing to ask. Now it does.
///
/// ★ ONE CALL FOR THE WHOLE PLAN. Three round trips would give the model three loose results to keep
/// straight, which is three chances to attach rule 2's number to rule 3.
/// </summary>
public sealed class SimulatePlanRulesTool(
    ISender sender,
    ILogger<SimulatePlanRulesTool> logger)
    : IAssistantTool
{
    public const string ToolName = "simulate_plan_rules";

    private const string RefusalMessage =
        "That plan could not be found, or it is not visible to this user.";

    public AssistantToolSchema Schema { get; } = new(
        ToolName,
        "Work out what each rule of a plan pays for a transaction amount or a number of units, using "
        + "the real commission engine, AND return how each rule is configured. Read-only; nothing is "
        + "created. Use it for any figure the user puts to you — \"I have a transaction of 7,850 with "
        + "5 units\", \"how much does each rule generate\", \"what would this pay\", \"if I sell X\" "
        + "— and never work the arithmetic out yourself. It answers the configuration question too, so "
        + "prefer it over the plan-rules lookup whenever an amount or a quantity is mentioned.",
        // Not strict, for the same reason the other tools are not: `additionalProperties: false` asks
        // the provider to validate the generated call, and a failure there is a 400 for the whole
        // request instead of a refusal the user can read.
        """
        {
          "type": "object",
          "properties": {
            "planId": {
              "type": "string",
              "description": "The plan's id, copied EXACTLY from an earlier answer in this conversation. Prefer it over planName once the plan has been looked up."
            },
            "planName": {
              "type": "string",
              "description": "The plan's COMPLETE name, verbatim from the user's message, including any prefix or parenthetical. Omit if the user did not name a plan."
            },
            "amount": {
              "type": "number",
              "description": "The hypothetical transaction amount, in the plan's currency."
            },
            "quantity": {
              "type": "integer",
              "description": "How many units the transaction covers. Only rules measured in Units use it; defaults to 1."
            },
            "attainmentPct": {
              "type": "number",
              "description": "Quota attainment as a RATIO (1.0 = quota reached), only if the user stated it. Omit when they did not: the tool then reports that the rule needs it, and you must ask rather than assume a value."
            },
            "priorCumulative": {
              "type": "number",
              "description": "Revenue already booked in the quota period, for split-at-quota rules. Omit unless the user stated it."
            },
            "quotaTarget": {
              "type": "number",
              "description": "The quota target amount, for split-at-quota rules. Omit unless the user stated it."
            }
          },
          "required": ["amount"]
        }
        """);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        // The payload's only destination is the prompt — never HTML, a URL or a script tag — and the
        // default encoder would turn every accented character of a Spanish plan name into six.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<string> RunAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        Arguments args;
        try
        {
            args = JsonSerializer.Deserialize<Arguments>(argumentsJson, Json) ?? new Arguments();
        }
        catch (JsonException)
        {
            logger.LogWarning("simulate_plan_rules: arguments were not valid JSON.");
            return Refusal();
        }

        if (args.Amount is null || args.Amount < 0m)
        {
            logger.LogWarning("simulate_plan_rules: missing or negative amount.");
            return Refusal();
        }

        Guid? planId = Guid.TryParse(args.PlanId, out var parsed) ? parsed : null;

        if (planId is null && string.IsNullOrWhiteSpace(args.PlanName))
        {
            return Refusal();
        }

        try
        {
            var result = await sender.Send(
                new SimulatePlanRulesQuery(
                    PlanId: planId,
                    PlanName: args.PlanName,
                    Amount: args.Amount.Value,
                    Quantity: args.Quantity is > 0 ? args.Quantity.Value : 1,
                    AttainmentPct: args.AttainmentPct,
                    PriorCumulative: args.PriorCumulative,
                    QuotaTarget: args.QuotaTarget),
                cancellationToken);

            if (!result.IsSuccess || result.Value is null)
            {
                // ★ NOT-FOUND AND NOT-PERMITTED TAKE THE SAME REPLY, so the answer cannot be used to
                // work out which one happened — the same refusal shape every other tool uses.
                logger.LogInformation("simulate_plan_rules: no plan resolved.");
                return Refusal();
            }

            // ★★ THE CONFIGURATION TRAVELS WITH THE CALCULATION, so ONE call answers a compound
            // question. Only one round of tool use is allowed per turn (AssistantToolRunner), so
            // "how is rule 2 configured and what would it pay?" would otherwise force the dispatcher
            // to pick a half and fail the other. This is the cheaper answer to that ceiling than
            // raising it.
            //
            // ★ AND IT IS THE SAME PROJECTION get_plan_rules USES, not a second description of the
            // same rule — see PlanRuleProjection. Two copies would be two answers about one rule, in
            // one context, the day they drifted.
            // ★ THE DESCRIPTION IS A BONUS AND MUST NEVER COST THE FIGURES. If this second read fails
            // for any reason, the answer still carries what each rule pays — losing the numbers
            // because the prose could not be assembled would be the tail wagging the dog. Caught
            // rather than guarded because a projection cast is one of the things that can throw.
            var configuration = new Dictionary<Guid, PlanRuleProjection.PlanRule>();
            try
            {
                var planDto = await sender.Send(
                    new GetPlanByIdQuery(result.Value.PlanId), cancellationToken);

                if (planDto?.IsSuccess == true && planDto.Value is not null)
                {
                    foreach (var rule in planDto.Value.Rules)
                    {
                        configuration[rule.Id] =
                            PlanRuleProjection.DescribeRule(rule, planDto.Value.Currency);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex, "simulate_plan_rules: the configuration could not be described; "
                        + "answering with the figures alone.");
            }

            return JsonSerializer.Serialize(Describe(result.Value, configuration), Json);
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogInformation("simulate_plan_rules: caller lacks Plans.Read.");
            return Refusal();
        }
    }

    private static string Refusal() =>
        JsonSerializer.Serialize(new RefusalPayload("NotFoundOrNotVisible", false, RefusalMessage), Json);

    /// <summary>
    /// ★ THE OUTPUT IS THE ENGINE'S, RESHAPED BUT NEVER RECOMPUTED. Steps keep the engine's order —
    /// rate, modifier, cap, then FLOOR — because that order is what makes a floor above a cap win, and
    /// a reader who reorders it "logically" arrives at a different number.
    /// </summary>
    private static PlanSimulation Describe(
        PlanSimulationDto dto,
        IReadOnlyDictionary<Guid, PlanRuleProjection.PlanRule> configuration) => new(
        Outcome: "PlanSimulation",
        Found: true,
        PlanId: dto.PlanId,
        PlanName: dto.PlanName,
        Currency: dto.Currency,
        SimulatedAmount: dto.Amount,
        SimulatedQuantity: dto.Quantity,
        // ★ NO TOTAL. Summing the rules would look like a payout and would not be one — a real payout
        // resolves which plan applies and can carry quota context and clawback. A sum printed here is
        // the number people would quote.
        Rules: dto.Rules.Select(r => new RuleSimulation(
            RuleId: r.RuleId,
            RuleName: r.RuleName,
            SortOrder: r.SortOrder,
            Computed: r.Simulated,
            // A token, never prose: the model renders the reason in the user's language.
            MissingContext: r.Blocker == RuleSimulationBlocker.None ? null : r.Blocker.ToString(),
            // ★ NULL, NOT ZERO, when the trigger did not match. "This rule does not apply to that
            // deal" and "it applies and pays nothing" are different answers and must stay different.
            GeneratesCredit: r.CreditGenerated,
            Commission: r.CreditGenerated ? r.CommissionAmount : null,
            // Null only if the plan became unreadable between the two queries — the answer then still
            // carries the figures rather than failing over a description.
            Configuration: configuration.GetValueOrDefault(r.RuleId),
            Steps: r.Steps.Select(s => new Step(
                Component: s.Component.ToString(),
                Outcome: s.Outcome.ToString(),
                In: s.InputAmount,
                Out: s.OutputAmount,
                Operand: s.Operand,
                Threshold: s.ThresholdAmount,
                // ★ WHERE THE ATTAINMENT CAME FROM TRAVELS ALL THE WAY OUT. Measured, Supplied or
                // Defaulted: a breakdown that says "attainment 100%" without saying who chose the
                // 100% repeats the engine's silent default in a more believable costume.
                AttainmentSource: s.AttainmentSource?.ToString())).ToList())).ToList());

    // ── Wire shapes ──────────────────────────────────────────────────────────

    private sealed class Arguments
    {
        public string? PlanId { get; set; }
        public string? PlanName { get; set; }
        public decimal? Amount { get; set; }
        public int? Quantity { get; set; }
        public decimal? AttainmentPct { get; set; }
        public decimal? PriorCumulative { get; set; }
        public decimal? QuotaTarget { get; set; }
    }

    private sealed record RefusalPayload(string Outcome, bool Found, string Message);

    private sealed record PlanSimulation(
        string Outcome, bool Found, Guid PlanId, string PlanName, string Currency,
        decimal SimulatedAmount, int SimulatedQuantity, IReadOnlyList<RuleSimulation> Rules);

    private sealed record RuleSimulation(
        Guid RuleId, string RuleName, int SortOrder, bool Computed, string? MissingContext,
        bool GeneratesCredit, decimal? Commission,
        // ★ WHAT THE RULE IS, beside what it paid: measurement, rate table with its SEMANTIC token,
        // trigger conditions, modifier, cap and floor. The semantic token is the field that stops
        // "€5.00 per unit" being read as 500% and a revenue bracket being read as quota attainment.
        PlanRuleProjection.PlanRule? Configuration,
        IReadOnlyList<Step> Steps);

    private sealed record Step(
        string Component, string Outcome, decimal? In, decimal? Out,
        decimal? Operand, decimal? Threshold, string? AttainmentSource);
}
