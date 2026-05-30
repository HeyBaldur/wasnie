using FluentAssertions;
using Wasnie.Infrastructure.Identity;

namespace Wasnie.IntegrationTests.BackgroundJobs;

public sealed class BackgroundJobTenantContextTests
{
    [Fact]
    public void TenantId_BeforeSetTenant_Throws()
    {
        var sut = new BackgroundJobTenantContext();

        var act = () => _ = sut.TenantId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SetTenant*");
    }

    [Fact]
    public void IsResolved_BeforeSetTenant_ReturnsFalse()
    {
        var sut = new BackgroundJobTenantContext();

        sut.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void SetTenant_ValidGuid_TenantIdReturnsValue()
    {
        var sut = new BackgroundJobTenantContext();
        var tenantId = Guid.NewGuid();

        sut.SetTenant(tenantId);

        sut.TenantId.Should().Be(tenantId);
        sut.IsResolved.Should().BeTrue();
    }

    [Fact]
    public void SetTenant_EmptyGuid_Throws()
    {
        var sut = new BackgroundJobTenantContext();

        var act = () => sut.SetTenant(Guid.Empty);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("tenantId");
    }

    [Fact]
    public void SetTenant_CalledTwice_SecondValueWins()
    {
        var sut = new BackgroundJobTenantContext();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        sut.SetTenant(first);
        sut.SetTenant(second);

        sut.TenantId.Should().Be(second);
    }
}
