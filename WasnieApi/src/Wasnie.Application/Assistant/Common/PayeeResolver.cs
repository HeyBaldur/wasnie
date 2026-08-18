using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Wasnie.Application.Assistant.Tools;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payees;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// HOW a payee was identified — and it is not a diagnostic, it is load-bearing.
///
/// ★ FOUND IN RUNTIME VERIFICATION. Asked for employee code NB-2001, the balance tool resolved it,
/// returned the full balance, and logged Found. The MODEL then answered "no payee with that name was
/// found": it had asked for "NB-2001", been handed a payload naming "Adrián Domínguez #2", and concluded
/// the answer was about somebody else. A real payee with real money reported as non-existent, from a
/// SUCCESSFUL lookup. The payload therefore says WHY this record is the answer, and rule 19a tells the
/// model to open with the full name rather than doubt the result.
/// </summary>
public enum PayeeMatch
{
    /// <summary>The full name, as typed (modulo dashes, spacing and case).</summary>
    ExactName = 0,

    /// <summary>An employee code — so the NAME in the payload will differ from what was asked.</summary>
    EmployeeCode = 1,

    /// <summary>No exact hit, and exactly one candidate contained it. Also a name the user did not type.</summary>
    PartialNameSingleCandidate = 2,

    /// <summary>
    /// The caller gave an id — from the resolved-entity context of this same conversation. Nothing was
    /// resolved and nothing could be mismatched, which is the entire point.
    /// </summary>
    ResolvedById = 3,

    /// <summary>
    /// SEVERAL payees answer to that name, and the resolver refused to pick one.
    ///
    /// ★ THIS IS NOT A NEW BEHAVIOUR — IT IS A NEWLY VISIBLE ONE. The refusal to guess between two
    /// people with the same name has always been here and is correct: choosing would put the wrong
    /// person's pay on screen. What was missing is that the refusal came back as <c>null</c>, landed in
    /// the same branch as "no such payee", and the user was told there was no record of somebody they
    /// were looking at on their own screen.
    /// </summary>
    Ambiguous = 4,
}

/// <summary>
/// What the resolver concluded. A record rather than a tuple because there are now THREE outcomes, not
/// two, and the third one carries the candidates the user has to choose between.
/// </summary>
/// <param name="Payee">The one payee, or null when none matched or several did.</param>
/// <param name="Match">How it was identified — or <see cref="PayeeMatch.Ambiguous"/>.</param>
/// <param name="Candidates">
/// The people who share the name, EMPTY unless <paramref name="Match"/> is
/// <see cref="PayeeMatch.Ambiguous"/>. Never a "did you mean" guess list for a name that matched
/// nothing: see <see cref="PayeeResolver"/> on why a near-miss list is a different feature.
/// </param>
public sealed record PayeeResolution(
    PayeeDto? Payee,
    PayeeMatch Match,
    IReadOnlyList<PayeeDto> Candidates)
{
    public static PayeeResolution Of(PayeeDto payee, PayeeMatch match) => new(payee, match, []);

    public static readonly PayeeResolution NotFound = new(null, PayeeMatch.ExactName, []);

    public static PayeeResolution Ambiguous(IReadOnlyList<PayeeDto> candidates) =>
        new(null, PayeeMatch.Ambiguous, candidates);
}

/// <summary>
/// Turning a name a model typed into one payee, shared by every payee-scoped tool.
///
/// ★ WHY IT IS SHARED RATHER THAN COPIED. This logic is four rules deep — exact name, then employee
/// code, then a single substring candidate, and an ambiguous match refused rather than guessed — and
/// every one of them was written in response to a specific way the answer came out wrong. A second copy
/// in the next tool would start identical and drift, and the drift would show up as two tools disagreeing
/// about who "Ana" is, in a product where that decides whose pay is on screen.
///
/// ★ NOTHING HERE AUTHORISES ANYTHING. It lists payees through the ordinary query, inside the caller's
/// request scope, so the tenant filter and <c>Payees.Read</c> apply on their own. The resource guard on
/// the payee's DATA lives in the query each tool sends afterwards, and must stay there.
/// </summary>
public static class PayeeResolver
{
    /// <summary>
    /// How many payees are scanned to resolve a name. Names are matched in memory because the payee
    /// list's search is a substring match and "Ana" must not silently resolve to "Ana María" when both
    /// exist — an exact match wins, and an ambiguous one is refused rather than guessed.
    /// </summary>
    public const int MaxPayeesScanned = 200;

    /// <summary>
    /// Exact match on full name or employee code, case-insensitively. An ambiguous name resolves to
    /// NOTHING rather than to the first row: two people called Ana García is an ordinary state of a
    /// company, and picking one of them would put the wrong person's pay in front of the reader.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The payee list query FAILED — which is not the same as returning no rows. Folding the two
    /// together would answer a broken lookup with a lie about the user's own data, so it is raised and
    /// the runner turns it into a retry card.
    /// </exception>
    public static async Task<PayeeResolution> ResolveAsync(
        ISender sender, string name, CancellationToken cancellationToken)
    {
        // ★ THE MODEL RETYPES THE IDENTIFIER, SO IT MUST BE COMPARED THE WAY A PERSON COMPARES NAMES.
        // Observed in runtime verification: asked about employee code NB-2001, the model wrote
        // "NB‑2001" with a NON-BREAKING HYPHEN. The raw string found nothing, and a real payee with real
        // money was reported as not existing. Reused rather than re-solved: the narrowing key is the
        // part of the string no typographic substitution can touch, so the row is FETCHED, and AreSame
        // then decides.
        //
        // (PlanNameMatch is named for plans because that is where the bug first surfaced. It is name
        // matching, not plan matching — renaming it is a separate change with its own blast radius.)
        var payees = await sender.Send(
            new ListPayeesQuery(new PaginationQuery
            {
                Page = 1,
                PageSize = MaxPayeesScanned,
                Search = PlanNameMatch.NarrowingKey(name) ?? name,
            }),
            cancellationToken);

        if (!payees.IsSuccess)
        {
            throw new InvalidOperationException($"The payee lookup failed: {payees.Error}");
        }

        var candidates = payees.Value?.Items ?? [];

        // Still an EXACT match, on both sides normalised — not a fuzzy one. "NB-2001" and "NB-2002"
        // stay different payees, which is the property that matters when the answer is somebody's pay.
        var byName = candidates.Where(p => PlanNameMatch.AreSame(p.FullName, name)).ToList();
        if (byName.Count == 1) return PayeeResolution.Of(byName[0], PayeeMatch.ExactName);
        if (byName.Count > 1) return PayeeResolution.Ambiguous(byName);

        var byCode = candidates.Where(p => PlanNameMatch.AreSame(p.EmployeeCode, name)).ToList();
        if (byCode.Count == 1) return PayeeResolution.Of(byCode[0], PayeeMatch.EmployeeCode);

        // Two people sharing an employee CODE should not happen and the resolver still does not guess.
        // It is reported as the same ambiguity rather than silently as "not found", because a duplicate
        // code is a data problem someone needs to see, not a missing person.
        if (byCode.Count > 1) return PayeeResolution.Ambiguous(byCode);

        // No exact hit: accept a single substring candidate, which is how "Ana" finds "Ana García" when
        // she is the only Ana.
        //
        // ★ AND SEVERAL SUBSTRING CANDIDATES STAY "NOT FOUND", DELIBERATELY. This branch is reached when
        // NOTHING carries the name the user typed — "Zoe Schmidt" against a tenant holding Anna and
        // Sergio Schmidt. Those are not people the user might have meant; they merely share a word with
        // a name that does not exist, and offering them as "did you mean" would be the resolver
        // guessing out loud. Ambiguity is reserved for the case where the name the user typed genuinely
        // belongs to more than one person.
        return candidates.Count == 1
            ? PayeeResolution.Of(candidates[0], PayeeMatch.PartialNameSingleCandidate)
            : PayeeResolution.NotFound;
    }
}

/// <summary>
/// THE ANSWER WHEN A NAME BELONGS TO MORE THAN ONE PERSON.
///
/// ★ WHY IT IS SHARED RATHER THAN WRITTEN TWICE. Both payee-scoped tools hit the same ambiguity through
/// the same resolver, and the system prompt teaches ONE rule about it. Two private copies of this payload
/// would start identical, drift, and the drift would show up as the assistant handling "which Anna?"
/// correctly for a balance and incorrectly for her assignments — for the same tenant, on adjacent turns.
///
/// ★ IT NAMES ONLY WHAT THE USER NEEDS TO CHOOSE: full name, employee code, employment status. No id
/// (rule 10b keeps ids off the page anyway), no email, no manager, and above all NO MONEY — the user has
/// not yet said whose balance they meant, so no balance may be shown.
///
/// ★ THE STATUS IS IN THE LIST ON PURPOSE. The reader is usually a finance analyst closing the books on
/// somebody who left, and "EPO9006, Terminated" versus "EMP406, Active" is exactly the fact that tells
/// them which Anna Schmidt they were asking about. Without it the two rows are distinguishable only by a
/// code the user has never seen.
///
/// ★ NOTHING NEW IS EXPOSED. The candidates come out of the same <c>ListPayeesQuery</c> that backs the
/// payees screen, inside the caller's request scope, so the tenant filter and <c>Payees.Read</c> decided
/// what is in this list. A user who cannot list payees never reaches here; one who can is being shown
/// names they can already read on that screen.
/// </summary>
public static class PayeeAmbiguity
{
    /// <summary>The token the system prompt branches on. Distinct from every refusal outcome.</summary>
    public const string Outcome = "AmbiguousPayee";

    /// <summary>
    /// How many candidates are listed. Four people sharing a name is real (this tenant has one such
    /// name); forty is a data problem, and pasting forty rows into a prompt so the model can read them
    /// aloud helps nobody. The count below always reports the true total.
    /// </summary>
    public const int MaxCandidatesListed = 10;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Payload(string requestedName, IReadOnlyList<PayeeDto> candidates) =>
        JsonSerializer.Serialize(
            new AmbiguousPayeePayload(
                Outcome: Outcome,
                // ★ FALSE BECAUSE NO SINGLE PAYEE WAS RESOLVED — not because nobody was found. The
                // distinction is the whole point of this payload, so the prompt states it in those
                // words: several people were found, which is why there is no answer yet.
                Found: false,
                RequestedName: requestedName,
                CandidateCount: candidates.Count,
                Candidates: candidates
                    .Take(MaxCandidatesListed)
                    .Select(p => new PayeeCandidate(p.FullName, p.EmployeeCode, p.StatusLabel))
                    .ToList(),
                Message:
                    "More than one payee answers to that name, so no balance, assignment or figure has "
                    + "been read for any of them. This is NOT a missing record: every person listed "
                    + "below exists. Tell the user which people share the name — giving each one's "
                    + "employee code and employment status — and ask them to reply with the employee "
                    + "code of the one they mean. Do not choose for them and do not answer about any of "
                    + "them until they say which."),
            Json);

    private sealed record AmbiguousPayeePayload(
        string Outcome,
        bool Found,
        string RequestedName,
        int CandidateCount,
        IReadOnlyList<PayeeCandidate> Candidates,
        string Message);

    /// <summary>Name to recognise them by, code to answer with, status to tell them apart.</summary>
    private sealed record PayeeCandidate(string FullName, string EmployeeCode, string Status);
}
