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
    public static async Task<(PayeeDto? Payee, PayeeMatch Match)> ResolveAsync(
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
        if (byName.Count == 1) return (byName[0], PayeeMatch.ExactName);
        if (byName.Count > 1) return (null, PayeeMatch.ExactName); // ambiguous — refuse rather than choose

        var byCode = candidates.Where(p => PlanNameMatch.AreSame(p.EmployeeCode, name)).ToList();
        if (byCode.Count == 1) return (byCode[0], PayeeMatch.EmployeeCode);
        if (byCode.Count > 1) return (null, PayeeMatch.EmployeeCode);

        // No exact hit: accept a single substring candidate, which is how "Ana" finds "Ana García" when
        // she is the only Ana. More than one and it is ambiguous again.
        return candidates.Count == 1
            ? (candidates[0], PayeeMatch.PartialNameSingleCandidate)
            : (null, PayeeMatch.ExactName);
    }
}
