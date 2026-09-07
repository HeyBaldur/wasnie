using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Common;

namespace Wasnie.Application.Assistant.Tools;

/// <summary>
/// The assistant asking the user which of ITS OWN functions to run, instead of guessing (KAN-58).
///
/// ★★ IT READS NOTHING AND CHANGES NOTHING, which is why it belongs on this interface. IAssistantTool's
/// contract is that a tool answers a question and cannot write; this one answers the question "what do
/// you actually want me to look up?" and touches no data at all. It is the only tool here whose subject
/// is the conversation rather than the tenant.
///
/// ★★ THE SHORTLIST IS THE MODEL'S JOB AND THE VALIDATION IS THIS CLASS'S. The ticket's first comment
/// is explicit that a fixed four-option menu is the wrong answer — the assistant must work out which
/// functions the question plausibly needs and offer only those. What it must NOT be trusted with is
/// whether those functions exist: a model that can invent a payout id can invent a function name, and
/// an option built from one would render a button that runs nothing. So every name is checked against
/// the registered tools and anything else is dropped.
///
/// ★ AND IF NOTHING SURVIVES, THERE IS NO FORM. The ticket's third comment draws that line: no relevant
/// function means an honest sentence, never a panel offering to look up a transaction to somebody who
/// asked about the weather.
/// </summary>
public sealed class AskUserToChooseTool(ILogger<AskUserToChooseTool> logger) : IAssistantTool
{
    public const string ToolName = "ask_user_to_choose";

    /// <summary>
    /// The functions an option may name.
    ///
    /// ★ THE TOOLS' OWN CONSTANTS, NOT COPIES. A second list of names here would agree with the real
    /// one until somebody renamed a tool, and the failure would be silent: an option that quietly stops
    /// being offered. Same argument ReconciliationReason makes about borrowing rather than redeclaring.
    /// </summary>
    private static readonly IReadOnlySet<string> Offerable = new HashSet<string>(StringComparer.Ordinal)
    {
        GetTransactionTool.ToolName,
        GetPlanRulesTool.ToolName,
        GetPayeeLedgerSummaryTool.ToolName,
        GetPayeePlansTool.ToolName,
        SimulatePlanRulesTool.ToolName,
    };

    /// <summary>
    /// camelCase, so the payload the model reads and the payload
    /// <see cref="AssistantClarify.Extract"/> parses are the same shape — they are the same string.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public AssistantToolSchema Schema { get; } = new(
        ToolName,
        "Ask the user to choose which of your functions to run, when you cannot answer their question "
        + "as asked. Use it in exactly two situations: the question is AMBIGUOUS between two of your "
        + "functions, or you were about to state something you cannot look up. Offer ONLY the functions "
        + "that plausibly serve THIS question — one or two is normal, three is the maximum, and offering "
        + "all of them means you have not thought about the question. Do NOT use it when none of your "
        + "functions is relevant: say plainly that you cannot help with that instead. Do NOT use it to "
        + "avoid running a lookup you could simply run.",
        // Not strict, for the same reason the other tools are not: a provider-side validation failure
        // is a 400 for the whole request instead of something the turn can recover from.
        """
        {
          "type": "object",
          "properties": {
            "options": {
              "type": "array",
              "description": "The functions worth offering for this question, most likely first. One or two is normal; never more than three.",
              "items": {
                "type": "object",
                "properties": {
                  "function": {
                    "type": "string",
                    "description": "The exact name of one of your functions.",
                    "enum": [
                      "get_transaction",
                      "get_plan_rules",
                      "get_payee_balance",
                      "get_payee_plans",
                      "simulate_plan_rules"
                    ]
                  },
                  "argument": {
                    "type": "string",
                    "description": "The name, reference or id the user ALREADY gave for this function, verbatim from their message. Omit it entirely if they did not give one — never guess a value."
                  }
                },
                "required": ["function"]
              }
            }
          },
          "required": ["options"]
        }
        """);

    public Task<string> RunAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        var requested = Parse(argumentsJson);

        var options = requested
            .Where(o => Offerable.Contains(o.Function))
            .DistinctBy(o => o.Function, StringComparer.Ordinal)
            .Take(ClarifyForm.MaxOptions)
            .ToList();

        if (options.Count < requested.Count)
        {
            // Worth a line: a model naming a function that does not exist is the same class of
            // invention this whole ticket is about, and it is invisible from the screen.
            logger.LogWarning(
                "{Tool}: dropped {Count} option(s) naming a function that is not registered.",
                ToolName, requested.Count - options.Count);
        }

        if (options.Count == 0)
        {
            logger.LogInformation("{Tool} finished: no offerable function, so no form.", ToolName);

            // ★ NO FORM, AND THE MODEL IS TOLD SO IN THE SAME BREATH. Returning an empty payload would
            // leave it to infer what happened; this tells it exactly which answer to write.
            return Task.FromResult(
                """
                {"clarifyOffered":false,"instruction":"No function of yours applies to this question. Do NOT show options. Say plainly and briefly that you cannot help with this one, and stop."}
                """);
        }

        logger.LogInformation("{Tool} finished: offering {Count} option(s).", ToolName, options.Count);

        var form = new ClarifyForm(options, ClarifyState.Open);

        // ★★ SERIALISED, NOT SPLICED. The first version built this by taking the clarify fragment and
        // calling Trim('{','}') on it to strip its braces — and Trim removes EVERY leading and trailing
        // character in the set, so it ate BOTH closing braces and produced unbalanced JSON. The form
        // then failed to parse and no panel was ever stored, silently, with the turn otherwise fine.
        // Building the object and letting the serialiser close it cannot go wrong that way.
        var payload = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["clarifyOffered"] = true,
                [AssistantClarify.PayloadKey] = form,

                // The instruction that stops the model printing the menu a second time in prose,
                // directly above the panel that already lists it.
                ["instruction"] =
                    "A form with these options has ALREADY been shown to the user. Write ONE short "
                    + "sentence asking them to pick one, or to rephrase. Do NOT list the options "
                    + "again, do NOT describe them, and do NOT answer the original question.",
            },
            Json);

        return Task.FromResult(payload);
    }

    /// <summary>
    /// Reads the model's arguments, tolerating everything a model does to JSON.
    ///
    /// ★ AN UNREADABLE CALL IS AN EMPTY FORM, NOT AN EXCEPTION. Every other tool here treats bad
    /// arguments as "nothing to offer" rather than a fault, because a thrown exception fails the whole
    /// turn — and failing a turn because the model garbled a request for a MENU is a terrible trade.
    /// </summary>
    private List<ClarifyOption> Parse(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return [];

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object) return [];
            if (!document.RootElement.TryGetProperty("options", out var array)) return [];
            if (array.ValueKind != JsonValueKind.Array) return [];

            var options = new List<ClarifyOption>();

            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                if (!element.TryGetProperty("function", out var function)) continue;
                if (function.ValueKind != JsonValueKind.String) continue;

                var argument = element.TryGetProperty("argument", out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString())
                        ? value.GetString()
                        : null;

                options.Add(new ClarifyOption(function.GetString()!, argument));
            }

            return options;
        }
        catch (JsonException)
        {
            logger.LogWarning("The assistant produced unreadable arguments for {Tool}.", ToolName);
            return [];
        }
    }
}
