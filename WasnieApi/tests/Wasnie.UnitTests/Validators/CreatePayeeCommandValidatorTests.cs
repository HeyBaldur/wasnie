using FluentAssertions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payees;
using Wasnie.Application.Compensation.Validators.Payees;

namespace Wasnie.UnitTests.Validators;

public sealed class CreatePayeeCommandValidatorTests
{
    private static CreatePayeeCommand ValidCommand(string? email = "user@example.com", DateOnly? hireDate = null) =>
        new("Test User", "EMP001", email, hireDate);

    private static CreatePayeeCommandValidator BuildValidator(bool emailRequired, bool hireDateRequired) =>
        new(new FakeFieldRequirementService(emailRequired, hireDateRequired));

    // ── FullName / EmployeeCode always required ───────────────────────────────

    [Fact]
    public async Task EmptyFullName_Fails()
    {
        var v = BuildValidator(false, false);
        var result = await v.ValidateAsync(ValidCommand() with { FullName = "" });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FullName");
    }

    [Fact]
    public async Task EmptyEmployeeCode_Fails()
    {
        var v = BuildValidator(false, false);
        var result = await v.ValidateAsync(ValidCommand() with { EmployeeCode = "" });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "EmployeeCode");
    }

    // ── Email: optional when setting is false ─────────────────────────────────

    [Fact]
    public async Task NullEmail_WithOptionalSetting_NoEmailError()
    {
        var v = BuildValidator(emailRequired: false, hireDateRequired: false);
        var result = await v.ValidateAsync(ValidCommand(email: null));
        result.Errors.Should().NotContain(e => e.PropertyName == "Email");
    }

    [Fact]
    public async Task NullEmail_WithRequiredSetting_Fails()
    {
        var v = BuildValidator(emailRequired: true, hireDateRequired: false);
        var result = await v.ValidateAsync(ValidCommand(email: null));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public async Task InvalidEmailFormat_WhenPresent_FailsRegardlessOfSetting()
    {
        var v = BuildValidator(emailRequired: false, hireDateRequired: false);
        var result = await v.ValidateAsync(ValidCommand(email: "not-an-email"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public async Task ValidEmail_WithRequiredSetting_NoEmailError()
    {
        var v = BuildValidator(emailRequired: true, hireDateRequired: false);
        var result = await v.ValidateAsync(ValidCommand(email: "valid@domain.com"));
        result.Errors.Should().NotContain(e => e.PropertyName == "Email");
    }

    // ── HireDate: optional when setting is false ──────────────────────────────

    [Fact]
    public async Task NullHireDate_WithOptionalSetting_NoHireDateError()
    {
        var v = BuildValidator(emailRequired: false, hireDateRequired: false);
        var result = await v.ValidateAsync(ValidCommand(hireDate: null));
        result.Errors.Should().NotContain(e => e.PropertyName == "HireDate");
    }

    [Fact]
    public async Task NullHireDate_WithRequiredSetting_Fails()
    {
        var v = BuildValidator(emailRequired: false, hireDateRequired: true);
        var result = await v.ValidateAsync(ValidCommand(hireDate: null));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "HireDate");
    }

    [Fact]
    public async Task FutureHireDate_WhenPresent_FailsRegardlessOfSetting()
    {
        var v = BuildValidator(emailRequired: false, hireDateRequired: false);
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var result = await v.ValidateAsync(ValidCommand(hireDate: future));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "HireDate");
    }

    [Fact]
    public async Task HireDateBefore1950_WhenPresent_Fails()
    {
        var v = BuildValidator(emailRequired: false, hireDateRequired: false);
        var result = await v.ValidateAsync(ValidCommand(hireDate: new DateOnly(1949, 12, 31)));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "HireDate");
    }

    [Fact]
    public async Task AllFieldsValid_BothOptional_Passes()
    {
        var v = BuildValidator(emailRequired: false, hireDateRequired: false);
        var result = await v.ValidateAsync(ValidCommand());
        result.IsValid.Should().BeTrue();
    }

    private sealed class FakeFieldRequirementService(bool emailRequired, bool hireDateRequired) : IFieldRequirementService
    {
        public Task<bool> IsRequiredAsync(string entityName, string fieldName, CancellationToken cancellationToken = default) =>
            Task.FromResult(fieldName == "Email" ? emailRequired : hireDateRequired);
    }
}
