using FluentAssertions;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;
using Wasnie.Domain.Compensation.Transactions;

namespace Wasnie.UnitTests.Services;

/// <summary>
/// These validators are the layer that turns a bad cell into the readable row error the import and
/// update wizards render. Both wizards share them by contract (see the type's own doc comment), so a
/// regression here silently degrades BOTH flows — hence covering them directly.
/// </summary>
public sealed class TransactionFieldValidatorsTests
{
    // ── Quantity ──────────────────────────────────────────────────────────────
    // Quantity became mappable from the import wizard; these are the rules the wizard now enforces.

    [Fact]
    public void ValidateQuantity_ValidInteger_ReturnsNoIssueAndParses()
    {
        var issue = TransactionFieldValidators.ValidateQuantity("50", out var parsed);

        issue.Should().BeNull();
        parsed.Should().Be(50);
    }

    // An unmapped column or an empty cell must fall back to 1, never fail the row — imports that
    // predate the quantity column have to keep working untouched.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateQuantity_Blank_DefaultsToOneWithoutIssue(string quantityStr)
    {
        var issue = TransactionFieldValidators.ValidateQuantity(quantityStr, out var parsed);

        issue.Should().BeNull();
        parsed.Should().Be(1);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public void ValidateQuantity_BelowOne_ReturnsReadableError(string quantityStr)
    {
        var issue = TransactionFieldValidators.ValidateQuantity(quantityStr, out _);

        issue.Should().NotBeNull();
        issue!.Severity.Should().Be(IssueSeverity.Error);
        issue.Field.Should().Be("quantity");
        issue.Message.Should().Contain("positive integer");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("2.5")]
    public void ValidateQuantity_NotAnInteger_ReturnsReadableError(string quantityStr)
    {
        var issue = TransactionFieldValidators.ValidateQuantity(quantityStr, out _);

        issue.Should().NotBeNull();
        issue!.Severity.Should().Be(IssueSeverity.Error);
        issue.Field.Should().Be("quantity");
        issue.Message.Should().Contain("valid integer");
    }

    // ── Description ───────────────────────────────────────────────────────────

    [Fact]
    public void ValidateDescription_NormalText_ReturnsNoIssue()
    {
        TransactionFieldValidators.ValidateDescription("Acme Contract 2026").Should().BeNull();
    }

    // The domain truncates rather than rejects, so this can only ever be a Warning — the row still
    // imports. The point of the warning is that the user is told before their text is shortened.
    [Fact]
    public void ValidateDescription_OverMaxLength_ReturnsTruncationWarningNotError()
    {
        var issue = TransactionFieldValidators.ValidateDescription(
            new string('x', CompensationTransaction.MaxDescriptionLength + 1));

        issue.Should().NotBeNull();
        issue!.Severity.Should().Be(IssueSeverity.Warning);
        issue.Message.Should().Contain("truncated");
    }
}
