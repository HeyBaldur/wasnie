using FluentAssertions;
using FluentValidation.TestHelper;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.Validators.Plans;

namespace Wasnie.UnitTests.Validators;

public sealed class CreatePlanCommandValidatorTests
{
    private readonly CreatePlanCommandValidator _sut = new();

    private static CreatePlanCommand Valid() => new(
        Name: "Enterprise Sales Q1 2025",
        Description: "Optional description",
        EffectiveStart: new DateOnly(2025, 1, 1),
        EffectiveEnd: new DateOnly(2025, 12, 31),
        Currency: "EUR");

    [Fact]
    public void ValidCommand_PassesValidation()
    {
        var result = _sut.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyName_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Name = "" });
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void NameExceeds200Chars_FailsValidation()
    {
        var longName = new string('x', 201);
        var result = _sut.TestValidate(Valid() with { Name = longName });
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Name200Chars_PassesValidation()
    {
        var exactName = new string('x', 200);
        var result = _sut.TestValidate(Valid() with { Name = exactName });
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void EmptyCurrency_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Currency = "" });
        result.ShouldHaveValidationErrorFor(x => x.Currency);
    }

    [Fact]
    public void CurrencyTwoChars_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Currency = "EU" });
        result.ShouldHaveValidationErrorFor(x => x.Currency);
    }

    [Fact]
    public void CurrencyFourChars_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Currency = "EURO" });
        result.ShouldHaveValidationErrorFor(x => x.Currency);
    }

    [Fact]
    public void EffectiveEndBeforeStart_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with
        {
            EffectiveStart = new DateOnly(2025, 12, 31),
            EffectiveEnd = new DateOnly(2025, 1, 1)
        });
        result.ShouldHaveValidationErrorFor(x => x.EffectiveEnd)
            .WithErrorMessage("EffectiveEnd must be on or after EffectiveStart.");
    }

    [Fact]
    public void EffectiveStartEqualsEnd_PassesValidation()
    {
        var date = new DateOnly(2025, 6, 15);
        var result = _sut.TestValidate(Valid() with { EffectiveStart = date, EffectiveEnd = date });
        result.ShouldNotHaveValidationErrorFor(x => x.EffectiveEnd);
    }

    [Fact]
    public void Description1000Chars_PassesValidation()
    {
        var result = _sut.TestValidate(Valid() with { Description = new string('d', 1000) });
        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void DescriptionExceeds1000Chars_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Description = new string('d', 1001) });
        result.ShouldHaveValidationErrorFor(x => x.Description);
    }
}
