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

        // ★★ WHO ACTUALLY BEARS THE NAME THE USER TYPED — and this is the distinction whose ABSENCE
        //    was the defect. The branch below used to be "exactly one substring candidate resolves,
        //    anything else is NOT FOUND", and it defended that with the Zoe example: "Zoe Schmidt"
        //    against a tenant holding Anna and Sergio Schmidt is not two people the user might have
        //    meant. That reasoning is right, and it does not cover the case that broke.
        //
        //    A tenant with Camille Laurent (EPO9009), Camille Laurent (EMP409) and Camille Martin
        //    (FR-301) was asked for "Camille". No full name IS "Camille", so the exact pass found
        //    nothing; three rows came back from the substring search, so the count was not one; and the
        //    resolver answered NOT FOUND. The user was told no payee called Camille exists while three
        //    of them were on their screen — the same lie the Ambiguous outcome was built to end, taking
        //    the other road into it.
        //
        // ★ THE TWO CASES ARE SEPARATED BY WHOLE-WORD CONTAINMENT, WHICH IS WHY IsPartialNameOf DOES IT
        //   RATHER THAN A NEW RULE. "Camille" is a complete word of "Camille Laurent", so each of those
        //   three genuinely IS a Camille and every one of them is a person the user might have meant.
        //   "Zoe Schmidt" is not a whole-word part of "Anna Schmidt" and "Anna Schmidt" is not one of
        //   it, so the Zoe case still bears nobody and still lands on NotFound, unchanged.
        var bearers = candidates.Where(p => PlanNameMatch.IsPartialNameOf(p.FullName, name)).ToList();

        // One Ana in the tenant still resolves straight to Ana García: a question with one possible
        // subject must never become a menu (§the clarify rule — never ask what you could answer).
        if (bearers.Count == 1) return PayeeResolution.Of(bearers[0], PayeeMatch.PartialNameSingleCandidate);

        // ★ N > 1 IS THE SAME REFUSAL AS TWO EXACT NAMESAKES, AND IT MUST PRODUCE THE SAME PAYLOAD.
        //   Fifteen Garcías is an ordinary state of a real company; the answer is fifteen people to
        //   choose from, never "no existe". Routing it through Ambiguous rather than a new outcome is
        //   what makes rule 23 cover it for free, for BOTH payee tools, with nothing to keep in step.
        if (bearers.Count > 1) return PayeeResolution.Ambiguous(bearers);

        // Nobody bears the name. The pre-existing fallback is left exactly as it was: a lone row from
        // the database's own substring search still resolves, so "Ana" finding only "Mariana López"
        // behaves today as it did yesterday. Narrowing the change to the N>1 branch keeps this fix off
        // every path that was not broken.
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
/// <summary>
/// THE SENTENCE THAT SAYS "THIS IS THE PERSON YOU ASKED FOR" — carried in the payload, next to the data.
///
/// ★★ THE FAILURE IT ENDS, OBSERVED THREE TIMES AND TWICE IN ONE AFTERNOON. The user picks an option
/// off the clarify form, the lookup RESOLVES and returns the balance (the log says <c>Found</c>), and
/// the assistant answers "I could not find any payee matching FR-301". Real money, for a real person,
/// reported as non-existent out of a SUCCESSFUL lookup. The same shape was recorded for NB-2001 in
/// <see cref="PayeeMatch"/>: asked for a code, handed a payload naming somebody, the model decides the
/// answer is about a different person and falls back to scenario 2C.
///
/// ★★ WHY THIS IS IN THE PAYLOAD AND NOT A SIXTH PROMPT RULE. Rule 19a already says exactly this —
/// "if found is true you FOUND the person; matchedBy tells you how; open with their full name" — and it
/// has now failed three times. A rule sitting a thousand tokens up the prompt competes with everything
/// else there; a sentence sitting INSIDE the JSON the model is reading at that moment does not. The
/// evidence for the difference is in this very file: <see cref="PayeeAmbiguity"/> embeds its
/// instruction the same way and its behaviour has been correct in every runtime test.
///
/// ★ IT IS ONLY EMITTED WHEN THE NAMES CANNOT MATCH. On an exact-name hit the payload already agrees
/// with the question and there is nothing to reconcile, so nothing is said — a reassurance repeated on
/// every turn is noise that teaches the model to skip the field.
/// </summary>
public static class PayeeMatchDisclosure
{
    /// <summary>
    /// The instruction for this match, or null when the answer needs none.
    /// </summary>
    /// <param name="match">How the resolver identified the payee.</param>
    /// <param name="requested">The term the user actually gave — a code, a partial name.</param>
    /// <param name="resolvedName">The payee's real full name, as the payload reports it.</param>
    public static string? For(PayeeMatch match, string? requested, string resolvedName)
    {
        if (string.IsNullOrWhiteSpace(requested)) return null;

        // ★ ExactName needs nothing: the question and the answer already use the same words.
        //   ResolvedById needs nothing either — an id the model copied from its own context is not a
        //   term it can mistake for somebody else's name.
        var kind = match switch
        {
            PayeeMatch.EmployeeCode => "employee code",
            PayeeMatch.PartialNameSingleCandidate => "part of the name",
            _ => null,
        };

        if (kind is null) return null;

        return $"\"{requested}\" IS the {kind} of {resolvedName}, and the figures below are THEIRS. "
            + "This lookup SUCCEEDED. You must NOT say the payee was not found, that nothing matched, "
            + $"that \"{requested}\" does not exist, or ask the user to check the spelling — they gave "
            + "a correct identifier and this is the person it belongs to. Open your answer with "
            + $"{resolvedName}'s full name so the user can see who it resolved to, then give the "
            + "figures.";
    }
}

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

    /// <param name="function">
    /// The tool that hit the ambiguity, so each option on the form re-runs THAT lookup once the user
    /// picks a person.
    ///
    /// ★ IT IS PASSED IN RATHER THAN ASSUMED, because both payee tools reach this same payload and the
    /// two must not offer each other's function: a user who asked for assignments and pressed a name
    /// must get assignments, not a balance.
    /// </param>
    public static string Payload(
        string requestedName, IReadOnlyList<PayeeDto> candidates, string function) =>
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
                    + "below exists. A FORM listing these people has ALREADY been shown to the user, so "
                    + "do NOT repeat the list in your answer: write ONE short sentence saying the name "
                    + "matches several people and asking which one they mean. Do not choose for them "
                    + "and do not answer about any of them until they say which.",

                // ★★ THE FORM IS BUILT HERE, FROM THE ROWS THE LOOKUP ACTUALLY MATCHED. Every option
                // carries an EMPLOYEE CODE as its argument rather than the name the user typed —
                // pressing one has to resolve to exactly one person, and the name is the very thing
                // that did not.
                Clarify: AssistantClarify.EntityForm(
                    candidates
                        .Take(ClarifyForm.MaxEntityOptions)
                        .Select(p => new ClarifyOption(
                            function,
                            p.EmployeeCode,
                            new ClarifyEntity(p.FullName, p.EmployeeCode, p.StatusLabel)))
                        .ToList(),
                    candidates.Count)),
            Json);

    private sealed record AmbiguousPayeePayload(
        string Outcome,
        bool Found,
        string RequestedName,
        int CandidateCount,
        IReadOnlyList<PayeeCandidate> Candidates,
        string Message,
        // The key the screen reads the panel from. Named explicitly rather than left to the camelCase
        // policy: this one has to equal AssistantClarify.PayloadKey, and a naming convention agreeing
        // with it today is not the same as it being the same string.
        [property: JsonPropertyName(AssistantClarify.PayloadKey)] ClarifyForm? Clarify);

    /// <summary>Name to recognise them by, code to answer with, status to tell them apart.</summary>
    private sealed record PayeeCandidate(string FullName, string EmployeeCode, string Status);
}
