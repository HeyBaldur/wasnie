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

    // ── Role: optional when setting is false ──────────────────────────────────

    [Fact]
    public async Task NullRole_WithOptionalSetting_NoRoleError()
    {
        var v = new CreatePayeeCommandValidator(new ConfigurableFieldService());
        var result = await v.ValidateAsync(ValidCommand() with { Role = null });
        result.Errors.Should().NotContain(e => e.PropertyName == "Role");
    }

    [Fact]
    public async Task NullRole_WithRequiredSetting_Fails()
    {
        var v = new CreatePayeeCommandValidator(new ConfigurableFieldService(role: true));
        var result = await v.ValidateAsync(ValidCommand() with { Role = null });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    // ── ManagerId: optional when setting is false ─────────────────────────────

    [Fact]
    public async Task NullManagerId_WithOptionalSetting_NoManagerError()
    {
        var v = new CreatePayeeCommandValidator(new ConfigurableFieldService());
        var result = await v.ValidateAsync(ValidCommand() with { ManagerId = null });
        result.Errors.Should().NotContain(e => e.PropertyName == "ManagerId");
    }

    [Fact]
    public async Task NullManagerId_WithRequiredSetting_Fails()
    {
        var v = new CreatePayeeCommandValidator(new ConfigurableFieldService(managerId: true));
        var result = await v.ValidateAsync(ValidCommand() with { ManagerId = null });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ManagerId");
    }

    // ── EmploymentType: optional when setting is false ────────────────────────

    [Fact]
    public async Task NullEmploymentType_WithOptionalSetting_NoError()
    {
        var v = new CreatePayeeCommandValidator(new ConfigurableFieldService());
        var result = await v.ValidateAsync(ValidCommand() with { EmploymentType = null });
        result.Errors.Should().NotContain(e => e.PropertyName == "EmploymentType");
    }

    [Fact]
    public async Task NullEmploymentType_WithRequiredSetting_Fails()
    {
        var v = new CreatePayeeCommandValidator(new ConfigurableFieldService(employmentType: true));
        var result = await v.ValidateAsync(ValidCommand() with { EmploymentType = null });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "EmploymentType");
    }

    [Fact]
    public async Task InvalidEmploymentType_WhenPresent_Fails()
    {
        var v = new CreatePayeeCommandValidator(new ConfigurableFieldService());
        var result = await v.ValidateAsync(ValidCommand() with { EmploymentType = "NotAType" });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "EmploymentType");
    }

    [Theory]
    [InlineData("FullTime")]
    [InlineData("fulltime")]
    [InlineData("Contractor")]
    [InlineData("Temporary")]
    [InlineData("PartTime")]
    public async Task ValidEmploymentType_Passes(string value)
    {
        var v = new CreatePayeeCommandValidator(new ConfigurableFieldService());
        var result = await v.ValidateAsync(ValidCommand() with { EmploymentType = value });
        result.Errors.Should().NotContain(e => e.PropertyName == "EmploymentType");
    }

    // ── Location: optional when setting is false ──────────────────────────────

    [Fact]
    public async Task NullLocation_WithOptionalSetting_NoError()
    {
        var v = new CreatePayeeCommandValidator(new ConfigurableFieldService());
        var result = await v.ValidateAsync(ValidCommand() with { Location = null });
        result.Errors.Should().NotContain(e => e.PropertyName == "Location");
    }

    [Fact]
    public async Task NullLocation_WithRequiredSetting_Fails()
    {
        var v = new CreatePayeeCommandValidator(new ConfigurableFieldService(location: true));
        var result = await v.ValidateAsync(ValidCommand() with { Location = null });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Location");
    }

    private sealed class FakeFieldRequirementService(bool emailRequired, bool hireDateRequired) : IFieldRequirementService
    {
        public Task<bool> IsRequiredAsync(string entityName, string fieldName, CancellationToken cancellationToken = default) =>
            Task.FromResult(fieldName == "Email" ? emailRequired : hireDateRequired);
    }

    private sealed class ConfigurableFieldService(
        bool email = false,
        bool hireDate = false,
        bool role = false,
        bool managerId = false,
        bool employmentType = false,
        bool location = false) : IFieldRequirementService
    {
        public Task<bool> IsRequiredAsync(string entityName, string fieldName, CancellationToken cancellationToken = default) =>
            Task.FromResult(fieldName switch
            {
                "Email" => email,
                "HireDate" => hireDate,
                "Role" => role,
                "ManagerId" => managerId,
                "EmploymentType" => employmentType,
                "Location" => location,
                _ => false,
            });
    }
}
