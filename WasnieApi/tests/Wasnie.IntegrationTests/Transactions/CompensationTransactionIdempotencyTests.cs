using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.IntegrationTests.Transactions;

[Collection(CompensationTransactionCollection.Name)]
public sealed class CompensationTransactionIdempotencyTests(CompensationTransactionFixture fixture)
{
    private static readonly DateOnly TxDate = new DateOnly(2024, 3, 1);
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 3, 2, 9, 0, 0, TimeSpan.Zero);

    // Each call returns a fresh Money instance so EF Core owned-entity
    // change tracking does not confuse different principals sharing the same object.
    private static CompensationTransaction MakeTx(Guid tenantId, string refNumber, string? externalId = null) =>
        CompensationTransaction.Ingest(
            tenantId, refNumber, Guid.NewGuid(), Money.Of(500m, "EUR"), TxDate,
            TransactionSource.CrmSync, "system",
            Guid.NewGuid(), Now, Guid.NewGuid(), externalId);

    // ── Idempotency index ─────────────────────────────────────────────────────

    [Fact]
    public async Task Insert_SameTenantSourceExternalId_SecondInsert_ThrowsUniqueViolation()
    {
        var tenantId = Guid.NewGuid();
        const string externalId = "CRM-DEAL-001";

        await using var db1 = fixture.CreateDbForTenant(tenantId);
        db1.CompensationTransactions.Add(MakeTx(tenantId, "REF-A", externalId));
        await db1.SaveChangesAsync();

        await using var db2 = fixture.CreateDbForTenant(tenantId);
        db2.CompensationTransactions.Add(MakeTx(tenantId, "REF-B", externalId));

        var act = async () => await db2.SaveChangesAsync();

        // SQL Server raises DbUpdateException wrapping a SqlException (2601 unique index
        // or 2627 unique constraint) when the filtered idempotency index is violated.
        var assertion = await act.Should().ThrowAsync<DbUpdateException>();
        assertion.WithInnerException<SqlException>()
            .Which.Number.Should().BeOneOf(2601, 2627);
    }

    [Fact]
    public async Task Insert_NullExternalId_MultipleTransactions_AllSucceed()
    {
        var tenantId = Guid.NewGuid();

        await using var db = fixture.CreateDbForTenant(tenantId);
        db.CompensationTransactions.Add(MakeTx(tenantId, "REF-X1", externalId: null));
        db.CompensationTransactions.Add(MakeTx(tenantId, "REF-X2", externalId: null));
        db.CompensationTransactions.Add(MakeTx(tenantId, "REF-X3", externalId: null));

        // Filtered index exempts null ExternalId rows — all three must succeed.
        var act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Insert_SameSourceExternalId_DifferentTenants_BothSucceed()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        const string externalId = "CRM-SHARED-999";

        await using var dbA = fixture.CreateDbForTenant(tenantA);
        dbA.CompensationTransactions.Add(MakeTx(tenantA, "REF-TA-1", externalId));
        await dbA.SaveChangesAsync();

        await using var dbB = fixture.CreateDbForTenant(tenantB);
        dbB.CompensationTransactions.Add(MakeTx(tenantB, "REF-TB-1", externalId));

        // Different tenant — unique key (TenantId, Source, ExternalId) differs.
        var act = async () => await dbB.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    // ── Multi-tenant global filter ────────────────────────────────────────────

    [Fact]
    public async Task Query_TransactionsForTenantA_DoesNotReturnTenantBData()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var dbInsert = fixture.CreateDbForTenant(tenantA);
        dbInsert.CompensationTransactions.Add(MakeTx(tenantA, "REF-FILTER-A"));
        await dbInsert.SaveChangesAsync();

        await using var dbInsertB = fixture.CreateDbForTenant(tenantB);
        dbInsertB.CompensationTransactions.Add(MakeTx(tenantB, "REF-FILTER-B"));
        await dbInsertB.SaveChangesAsync();

        await using var dbQueryA = fixture.CreateDbForTenant(tenantA);
        var results = await dbQueryA.CompensationTransactions.ToListAsync();

        results.Should().OnlyContain(t => t.TenantId == tenantA,
            "global query filter must scope reads to the current tenant");
        results.Should().NotContain(t => t.TenantId == tenantB);
    }

    [Fact]
    public async Task Query_TransactionsForTenantB_DoesNotReturnTenantAData()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var dbA = fixture.CreateDbForTenant(tenantA);
        dbA.CompensationTransactions.Add(MakeTx(tenantA, "REF-XFIL-A"));
        await dbA.SaveChangesAsync();

        await using var dbB = fixture.CreateDbForTenant(tenantB);
        var results = await dbB.CompensationTransactions.ToListAsync();

        results.Should().NotContain(t => t.TenantId == tenantA,
            "tenant B context must not see tenant A data");
    }
}
