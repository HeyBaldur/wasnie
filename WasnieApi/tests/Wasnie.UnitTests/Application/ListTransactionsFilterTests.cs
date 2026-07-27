using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Handlers.Transactions;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Text search filters by ReferenceNumber OR deal name (Description) in the SAME input — case-insensitive,
/// null-Description-safe. Scope is Reference + Description only (not ProductName/Sku/Category/Payee).
/// EF InMemory runs these as LINQ-to-Objects, so the null-guard behaviour is genuinely exercised.
/// </summary>
public sealed class ListTransactionsFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContext NewDb(string name, Guid tenantId)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());
    }

    private static ListTransactionsHandler NewHandler(ApplicationDbContext db) =>
        new(db, Substitute.For<IAuthorizationService>());

    private static CompensationTransaction Tx(Guid tenantId, string reference, string? description) =>
        CompensationTransaction.Ingest(tenantId, reference, Guid.NewGuid(), Money.Of(100m, "EUR"),
            new DateOnly(2026, 6, 1), TransactionSource.Manual, "seed", Guid.NewGuid(), Now, Guid.NewGuid(),
            description: description);

    private static async Task<ApplicationDbContext> SeedAsync(string name, Guid tenant)
    {
        var db = NewDb(name, tenant);
        db.CompensationTransactions.AddRange(
            Tx(tenant, "HUBSPOT-512460112106-473111097540", "E2E-C"),
            Tx(tenant, "HUBSPOT-999", "E2E-A"),
            Tx(tenant, "MANUAL-001", null)); // no deal name
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<List<string>> SearchRefsAsync(ApplicationDbContext db, string term)
    {
        var result = await NewHandler(db).Handle(
            new ListTransactionsQuery(new PaginationQuery { Reference = term, Page = 1, PageSize = 50 }), default);
        result.IsSuccess.Should().BeTrue();
        return result.Value!.Items.Select(i => i.ReferenceNumber).ToList();
    }

    [Fact]
    public async Task Matches_by_reference_number()
    {
        var t = Guid.NewGuid();
        var db = await SeedAsync(nameof(Matches_by_reference_number), t);
        (await SearchRefsAsync(db, "512460112106"))
            .Should().ContainSingle().Which.Should().Be("HUBSPOT-512460112106-473111097540");
    }

    [Fact]
    public async Task Matches_by_deal_name_description()
    {
        var t = Guid.NewGuid();
        var db = await SeedAsync(nameof(Matches_by_deal_name_description), t);
        (await SearchRefsAsync(db, "E2E-C"))
            .Should().ContainSingle().Which.Should().Be("HUBSPOT-512460112106-473111097540");
    }

    [Fact]
    public async Task Matches_either_reference_or_description()
    {
        var t = Guid.NewGuid();
        var db = await SeedAsync(nameof(Matches_either_reference_or_description), t);
        // "E2E" lives in two descriptions; "HUBSPOT" lives in two references.
        (await SearchRefsAsync(db, "E2E"))
            .Should().BeEquivalentTo(new[] { "HUBSPOT-512460112106-473111097540", "HUBSPOT-999" });
        (await SearchRefsAsync(db, "HUBSPOT")).Should().HaveCount(2);
    }

    [Fact]
    public async Task Is_case_insensitive_on_both_fields()
    {
        var t = Guid.NewGuid();
        var db = await SeedAsync(nameof(Is_case_insensitive_on_both_fields), t);
        (await SearchRefsAsync(db, "e2e-c")).Should().ContainSingle();       // matches description "E2E-C"
        (await SearchRefsAsync(db, "hubspot-999")).Should().ContainSingle(); // matches reference "HUBSPOT-999"
    }

    [Fact]
    public async Task Null_description_does_not_throw_and_still_matches_by_reference()
    {
        var t = Guid.NewGuid();
        var db = await SeedAsync(nameof(Null_description_does_not_throw_and_still_matches_by_reference), t);
        // The null-Description row must be searchable by its reference and must not NRE on the null field.
        (await SearchRefsAsync(db, "MANUAL"))
            .Should().ContainSingle().Which.Should().Be("MANUAL-001");
    }

    [Fact]
    public async Task No_match_returns_empty()
    {
        var t = Guid.NewGuid();
        var db = await SeedAsync(nameof(No_match_returns_empty), t);
        (await SearchRefsAsync(db, "NONEXISTENT-XYZ")).Should().BeEmpty();
    }
}
