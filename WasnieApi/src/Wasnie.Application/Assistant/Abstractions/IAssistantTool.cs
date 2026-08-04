namespace Wasnie.Application.Assistant.Abstractions;

/// <summary>
/// A tool as the model is told about it: a name, what it is for, and a JSON Schema for its arguments.
/// </summary>
/// <param name="ParametersJson">
/// A JSON Schema object. Kept as a string rather than a typed tree because it is written once per tool
/// and read only by the provider — modelling it would be a schema library nobody asked for.
/// </param>
public sealed record AssistantToolSchema(string Name, string Description, string ParametersJson);

/// <summary>What the model asked for: a tool by name, with its arguments as the model wrote them.</summary>
public sealed record AssistantToolRequest(string Name, string ArgumentsJson);

/// <summary>
/// A read-only lookup the assistant may run on the user's behalf.
///
/// ★ READ-ONLY IS A PROPERTY OF THE INTERFACE, not a convention. There is no Write, no Execute and no
/// Apply here, and there must never be: an assistant that can change what people are paid is a
/// different product with a different risk profile, and the way that arrives is one innocuous-looking
/// method on a contract that already exists. A tool answers questions. That is all it can do.
///
/// ★ EVERY TOOL RUNS THROUGH THE DOMAIN, WITH THE ASKING USER'S IDENTITY. Implementations send the
/// SAME queries the screens send, inside the caller's request scope — so the tenant filter and the
/// permission guards apply on their own, unchanged and untested-around. A tool that reached for the
/// DbContext directly would be re-implementing authorisation by accident, and would get it wrong the
/// first time somebody added a role.
/// </summary>
public interface IAssistantTool
{
    /// <summary>How the model is told this tool exists.</summary>
    AssistantToolSchema Schema { get; }

    /// <summary>
    /// Runs the lookup and returns JSON for the model to read.
    ///
    /// Never throws for "not found" or "not allowed" — both come back as the same refusal payload, so
    /// the answer cannot be used to work out which one happened. See the implementation.
    /// </summary>
    Task<string> RunAsync(string argumentsJson, CancellationToken cancellationToken);
}
