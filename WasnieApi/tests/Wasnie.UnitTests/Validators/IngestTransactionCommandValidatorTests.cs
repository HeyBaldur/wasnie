using FluentValidation.TestHelper;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Application.Compensation.Validators.Transactions;

namespace Wasnie.UnitTests.Validators;

public sealed class IngestTransactionCommandValidatorTests
{
    private readonly IngestTransactionCommandValidator _sut = new();

    private static IngestTransactionCommand Valid() => new(
        ReferenceNumber: "TXN-2025-001",
        PayeeId: Guid.NewGuid(),
        Amount: 1500.00m,
        Currency: "EUR",
        TransactionDate: new DateOnly(2025, 6, 15));

    [Fact]
    public void ValidCommand_PassesValidation()
    {
        var result = _sut.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyReferenceNumber_FailsValidation(string? refNum)
    {
        var result = _sut.TestValidate(Valid() with { ReferenceNumber = refNum! });
        result.ShouldHaveValidationErrorFor(x => x.ReferenceNumber);
    }

    [Fact]
    public void ReferenceNumber201Chars_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { ReferenceNumber = new string('x', 201) });
        result.ShouldHaveValidationErrorFor(x => x.ReferenceNumber);
    }

    [Fact]
    public void ReferenceNumber200Chars_PassesValidation()
    {
        var result = _sut.TestValidate(Valid() with { ReferenceNumber = new string('x', 200) });
        result.ShouldNotHaveValidationErrorFor(x => x.ReferenceNumber);
    }

    [Fact]
    public void EmptyPayeeId_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { PayeeId = Guid.Empty });
        result.ShouldHaveValidationErrorFor(x => x.PayeeId);
    }

    [Fact]
    public void NullPayeeId_PassesValidation_OptionalityEnforcedByHandler()
    {
        // Decision D: null PayeeId passes the validator; handler enforces optionality via field requirements.
        var result = _sut.TestValidate(Valid() with { PayeeId = null });
        result.ShouldNotHaveValidationErrorFor(x => x.PayeeId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.001)]
    public void ZeroOrNegativeAmount_FailsValidation(double amount)
    {
        var result = _sut.TestValidate(Valid() with { Amount = (decimal)amount });
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(100)]
    [InlineData(999999.9999)]
    public void PositiveAmount_PassesValidation(double amount)
    {
        var result = _sut.TestValidate(Valid() with { Amount = (decimal)amount });
        result.ShouldNotHaveValidationErrorFor(x => x.Amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("EU")]
    [InlineData("EURO")]
    public void InvalidCurrencyLength_FailsValidation(string currency)
    {
        var result = _sut.TestValidate(Valid() with { Currency = currency });
        result.ShouldHaveValidationErrorFor(x => x.Currency);
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("GBP")]
    public void ValidCurrency_PassesValidation(string currency)
    {
        var result = _sut.TestValidate(Valid() with { Currency = currency });
        result.ShouldNotHaveValidationErrorFor(x => x.Currency);
    }

    [Fact]
    public void TransactionDateBefore2000_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { TransactionDate = new DateOnly(1999, 12, 31) });
        result.ShouldHaveValidationErrorFor(x => x.TransactionDate)
            .WithErrorMessage("TransactionDate cannot be before 2000-01-01.");
    }

    [Fact]
    public void TransactionDateExactly2000Jan01_PassesValidation()
    {
        var result = _sut.TestValidate(Valid() with { TransactionDate = new DateOnly(2000, 1, 1) });
        result.ShouldNotHaveValidationErrorFor(x => x.TransactionDate);
    }
}
