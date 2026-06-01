using FluentAssertions;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

public sealed class PayeeTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static Payee CreateValid(string? email = "user@example.com", DateOnly? hireDate = null) =>
        Payee.Create(
            TenantId, "Test User", "EMP001", email,
            hireDate ?? new DateOnly(2020, 1, 1),
            "admin", Guid.NewGuid(), Now);

    // ── Email nullable ────────────────────────────────────────────────────────

    [Fact]
    public void Create_NullEmail_Succeeds_WhenEmailIsOptional()
    {
        var payee = Payee.Create(TenantId, "Test User", "EMP001", null,
            new DateOnly(2020, 1, 1), "admin", Guid.NewGuid(), Now);
        payee.Email.Should().BeNull();
    }

    [Fact]
    public void Create_EmptyEmail_StoredAsNull()
    {
        var payee = Payee.Create(TenantId, "Test User", "EMP001", "   ",
            new DateOnly(2020, 1, 1), "admin", Guid.NewGuid(), Now);
        payee.Email.Should().BeNull();
    }

    [Fact]
    public void Create_EmailProvided_NormalizesToLowercase()
    {
        var payee = CreateValid("USER@Example.COM");
        payee.Email.Should().Be("user@example.com");
    }

    // ── HireDate nullable ─────────────────────────────────────────────────────

    [Fact]
    public void Create_NullHireDate_Succeeds_WhenHireDateIsOptional()
    {
        var payee = Payee.Create(TenantId, "Test User", "EMP001", null,
            null, "admin", Guid.NewGuid(), Now);
        payee.HireDate.Should().BeNull();
    }

    [Fact]
    public void Create_FutureHireDate_ThrowsDomainException()
    {
        var future = DateOnly.FromDateTime(Now.UtcDateTime.AddDays(1));
        var act = () => Payee.Create(TenantId, "Test User", "EMP001", null,
            future, "admin", Guid.NewGuid(), Now);
        act.Should().Throw<DomainException>().WithMessage("*future*");
    }

    [Fact]
    public void Create_PastHireDate_Succeeds()
    {
        var payee = CreateValid(hireDate: new DateOnly(1990, 3, 15));
        payee.HireDate.Should().Be(new DateOnly(1990, 3, 15));
    }

    // ── Always-required fields still enforced ─────────────────────────────────

    [Fact]
    public void Create_BlankFullName_ThrowsDomainException()
    {
        var act = () => Payee.Create(TenantId, "  ", "EMP001", null, null,
            "admin", Guid.NewGuid(), Now);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_BlankEmployeeCode_ThrowsDomainException()
    {
        var act = () => Payee.Create(TenantId, "Test User", "   ", null, null,
            "admin", Guid.NewGuid(), Now);
        act.Should().Throw<DomainException>();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_NullEmail_SetsToNull()
    {
        var payee = CreateValid("original@example.com");
        payee.Update("Test User", "EMP001", null, new DateOnly(2020, 1, 1),
            null, null, "admin", Now.AddHours(1));
        payee.Email.Should().BeNull();
    }

    [Fact]
    public void Update_NullHireDate_SetsToNull()
    {
        var payee = CreateValid();
        payee.Update("Test User", "EMP001", "user@example.com", null,
            null, null, "admin", Now.AddHours(1));
        payee.HireDate.Should().BeNull();
    }
}
