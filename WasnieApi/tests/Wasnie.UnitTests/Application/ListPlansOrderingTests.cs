using FluentAssertions;
using NSubstitute;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Handlers.Plans;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The plans list defaults to creation order, newest first (the most recently created plan on top).
/// The frontend store now requests sortBy=createdat/desc; this proves the handler honours it.
/// </summary>
public sealed class ListPlansOrderingTests
{
    private static (ApplicationDbContext Db, ListPlansHandler Handler) Build(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        // Three plans created at distinct times. Names are intentionally NOT in creation order, so an
        // alphabetical (old default) result would differ from a creation-ordered one.
        Plan Make(string name, DateTimeOffset createdAt) => Plan.Create(
            tenantId, name, "desc", DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "EUR", "seed", Guid.NewGuid(), createdAt, Guid.NewGuid());

        db.CompensationPlans.Add(Make("Zeta", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));  // oldest
        db.CompensationPlans.Add(Make("Alpha", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));  // middle
        db.CompensationPlans.Add(Make("Mike", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));  // newest
        db.SaveChanges();

        var auth = Substitute.For<IAuthorizationService>();
        return (db, new ListPlansHandler(db, auth));
    }

    [Fact]
    public async Task Default_createdat_desc_puts_the_newest_plan_first()
    {
        var (_, handler) = Build(nameof(Default_createdat_desc_puts_the_newest_plan_first));

        var result = await handler.Handle(
            new ListPlansQuery(new PaginationQuery { SortBy = "createdat", SortOrder = "desc" }), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Select(i => i.Name).Should().ContainInOrder("Mike", "Alpha", "Zeta");
    }

    [Fact]
    public async Task Createdat_asc_puts_the_oldest_plan_first()
    {
        var (_, handler) = Build(nameof(Createdat_asc_puts_the_oldest_plan_first));

        var result = await handler.Handle(
            new ListPlansQuery(new PaginationQuery { SortBy = "createdat", SortOrder = "asc" }), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Select(i => i.Name).Should().ContainInOrder("Zeta", "Alpha", "Mike");
    }
}
