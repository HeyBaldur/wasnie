using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Enrichment;
using Wasnie.Application.Compensation.Handlers.Enrichment;
using Wasnie.Domain.Compensation.Enrichment;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Enrichment;

/// <summary>
/// Collision is a HARD error, not silent precedence — two mappings for the same (field, value) would
/// make enrichment ambiguous, the very silence this layer exists to remove.
/// </summary>
public sealed class CreateCategoryMappingHandlerTests
{
    private static (ApplicationDbContext Db, CreateCategoryMappingHandler Handler) Build(Guid tenantId)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"cat-map-{Guid.NewGuid()}").Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        var handler = new CreateCategoryMappingHandler(
            db, tenantCtx, new FakeGuidGenerator(), Substitute.For<IAuthorizationService>());

        return (db, handler);
    }

    [Fact]
    public async Task First_mapping_is_created()
    {
        var (_, handler) = Build(Guid.NewGuid());

        var result = await handler.Handle(
            new CreateCategoryMappingCommand(CategoryMapping.Fields.ProductName, "LAP-12", "Laptops"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.InputField.Should().Be("ProductName");
        result.Value.InputValue.Should().Be("LAP-12");
        result.Value.Category.Should().Be("Laptops");
    }

    // (e) A second mapping with the SAME (InputField, InputValue) is rejected.
    [Fact]
    public async Task Duplicate_field_and_value_is_rejected()
    {
        var (_, handler) = Build(Guid.NewGuid());

        var first = await handler.Handle(
            new CreateCategoryMappingCommand(CategoryMapping.Fields.ProductName, "LAP-12", "Laptops"), default);
        first.IsSuccess.Should().BeTrue();

        var second = await handler.Handle(
            new CreateCategoryMappingCommand(CategoryMapping.Fields.ProductName, "LAP-12", "Something Else"), default);

        second.IsSuccess.Should().BeFalse();
        second.Error.Should().Contain("already exists");
    }

    // The same input VALUE under a DIFFERENT field is a distinct mapping — allowed.
    [Fact]
    public async Task Same_value_under_a_different_field_is_allowed()
    {
        var (_, handler) = Build(Guid.NewGuid());

        var byName = await handler.Handle(
            new CreateCategoryMappingCommand(CategoryMapping.Fields.ProductName, "LAP-12", "Laptops"), default);
        var bySku = await handler.Handle(
            new CreateCategoryMappingCommand(CategoryMapping.Fields.ProductSku, "LAP-12", "Laptops"), default);

        byName.IsSuccess.Should().BeTrue();
        bySku.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_input_field_is_rejected()
    {
        var (_, handler) = Build(Guid.NewGuid());

        var result = await handler.Handle(
            new CreateCategoryMappingCommand("DealType", "New Logo", "Enterprise"), default);

        result.IsSuccess.Should().BeFalse();
    }
}
