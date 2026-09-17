using FluentAssertions;
using Wasnie.Application.Compensation.Common;

namespace Wasnie.UnitTests.Compensation;

/// <summary>
/// KAN-81: a reference copied off the screen must find the row, whatever invisible characters the
/// copy dragged along.
///
/// ★★ THE CASE THESE EXIST FOR is <c>HUBSPOT-517982827731</c>: stored with a plain U+002D, searched
/// with a U+2011 the assistant itself had written, and reported to the user as non-existent. The two
/// strings render identically, which is why the user was told three times to "copy it exactly".
///
/// ★ ONLY THE INPUT IS FOLDED, and the tests say so on purpose: there is no test asserting that a
/// dirty STORED value is cleaned, because nothing cleans it. Of 10,827 rows, zero carry a non-ASCII
/// character; folding the column would cost the index and move a write-time concern onto the read
/// path. If that measurement ever changes, the fix is in ingestion and these tests should start
/// failing to say so.
/// </summary>
public sealed class Kan81ReferenceNormalizationTests
{
    private const string Plain = "HUBSPOT-517982827731";

    [Theory]
    [InlineData('‐')] // hyphen
    [InlineData('‑')] // non-breaking hyphen — the incident
    [InlineData('‒')] // figure dash
    [InlineData('–')] // en dash
    [InlineData('—')] // em dash
    [InlineData('―')] // horizontal bar
    [InlineData('−')] // minus sign
    [InlineData('－')] // fullwidth hyphen-minus
    public void EveryDashLikeCharacterFoldsToThePlainHyphen(char dash)
    {
        var pasted = $"HUBSPOT{dash}517982827731";

        ReferenceSearchNormalizer.Normalize(pasted).Should().Be(Plain);
    }

    [Fact]
    public void TheIncidentReferenceMatchesTheStoredOneAfterFolding()
    {
        // Exactly what happened: the stored side is plain, the searched side came out of the
        // assistant's own reply carrying U+2011.
        const string fromZeke = "HUBSPOT‑517982827731";

        ReferenceSearchNormalizer.Normalize(fromZeke)
            .Should().Be(ReferenceSearchNormalizer.Normalize(Plain));
    }

    [Fact]
    public void ZeroWidthCharactersAreRemovedRatherThanTurnedIntoSpaces()
    {
        // They have no width, so the user never saw anything there to type.
        const string pasted = "HUBSPOT​-﻿517982827731";

        ReferenceSearchNormalizer.Normalize(pasted).Should().Be(Plain);
    }

    [Fact]
    public void NonBreakingSpacesBecomeOrdinarySpacesAndAreNotDropped()
    {
        // A space HAS width: removing it would silently join two words the user sees as separate.
        ReferenceSearchNormalizer.Normalize("EU Standard").Should().Be("EU Standard");
    }

    [Fact]
    public void TheEndsAreTrimmedIncludingInvisiblePadding()
    {
        ReferenceSearchNormalizer.Normalize("  HUBSPOT-517982827731  ").Should().Be(Plain);
    }

    [Fact]
    public void APlainReferenceIsLeftExactlyAsItWas()
    {
        ReferenceSearchNormalizer.Normalize(Plain).Should().Be(Plain);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankInputComesBackUnchangedSoCallersKeepTheirOwnNoFilterCheck(string? term)
    {
        ReferenceSearchNormalizer.Normalize(term).Should().Be(term);
    }

    [Fact]
    public void FoldingDoesNotTouchTheDigitsOrTheLetters()
    {
        // The guard against an over-eager normaliser: only the separators may change.
        ReferenceSearchNormalizer.Normalize("HUBSPOT‑517982827731")
            .Should().Be(Plain)
            .And.HaveLength(Plain.Length);
    }
}
