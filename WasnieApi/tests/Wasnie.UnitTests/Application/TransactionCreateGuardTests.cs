using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Common;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// PASO 4 — the centralized create rule (shared by HubSpot/Excel/Manual). Active key → skip; only a void →
/// create (Opción B); void with credits → blocked (anti-double-pay). Reference is tenant-wide; ExternalId
/// is per source.
/// </summary>
public sealed class TransactionCreateGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 10, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContext NewDb(string name, Guid tenantId)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
            ctx, Substitute.For<MediatR.IPublisher>());
    }

    private static CompensationTransaction Seed(
        ApplicationDbContext db, Guid tenantId, string reference, TransactionSource source,
        string? externalId, bool cancelled)
    {
        var tx = CompensationTransaction.Ingest(tenantId, reference, Guid.NewGuid(), Money.Of(100m, "EUR"),
            new DateOnly(2026, 6, 1), source, "seed", Guid.NewGuid(), Now, Guid.NewGuid(), externalId: externalId);
        if (cancelled) tx.Cancel("voided in test", "seed", Now, Guid.NewGuid());
        db.CompensationTransactions.Add(tx);
        db.SaveChanges();
        return tx;
    }

    private static void AddCredit(ApplicationDbContext db, Guid tenantId, CompensationTransaction tx)
    {
        var ruleId = Guid.NewGuid();
        db.Credits.Add(Credit.Allocate(tenantId, tx.Id, tx.PayeeId!.Value, Guid.NewGuid(), ruleId,
            RuleSnapshot.Freeze(ruleId, Guid.NewGuid(), 1, "Base", RateTable.Flat(0.05m), Trigger.Always(), Now),
            Money.Of(100m, "EUR"), Money.Of(5m, "EUR"), Percentage.FromPercent(5m), CreditRole.Primary,
            "seed", Guid.NewGuid(), Now, Guid.NewGuid()));
        db.SaveChanges();
    }

    private static async Task<TransactionCreateDecision> Decide(
        ApplicationDbContext db, TransactionSource source, string reference, string? externalId)
    {
        var classification = await new TransactionCreateGuard(db)
            .ClassifyAsync(source, [reference], [externalId], default);
        return classification.Decide(reference, externalId);
    }

    [Fact]
    public async Task No_existing_transaction_with_the_key_allows_create()
    {
        var t = Guid.NewGuid();
        var db = NewDb(nameof(No_existing_transaction_with_the_key_allows_create), t);
        (await Decide(db, TransactionSource.Manual, "REF-NEW", null))
            .Should().Be(TransactionCreateDecision.Create);
    }

    [Fact]
    public async Task Active_transaction_with_same_reference_is_skipped_as_duplicate()
    {
        var t = Guid.NewGuid();
        var db = NewDb(nameof(Active_transaction_with_same_reference_is_skipped_as_duplicate), t);
        Seed(db, t, "REF-1", TransactionSource.Manual, null, cancelled: false);
        (await Decide(db, TransactionSource.Manual, "REF-1", null))
            .Should().Be(TransactionCreateDecision.SkipActiveDuplicate);
    }

    [Fact]
    public async Task Only_a_voided_transaction_with_the_key_allows_create()
    {
        var t = Guid.NewGuid();
        var db = NewDb(nameof(Only_a_voided_transaction_with_the_key_allows_create), t);
        Seed(db, t, "REF-1", TransactionSource.Manual, null, cancelled: true);
        (await Decide(db, TransactionSource.Manual, "REF-1", null))
            .Should().Be(TransactionCreateDecision.Create);
    }

    [Fact]
    public async Task Voided_transaction_that_had_credits_is_blocked()
    {
        var t = Guid.NewGuid();
        var db = NewDb(nameof(Voided_transaction_that_had_credits_is_blocked), t);
        var tx = Seed(db, t, "REF-1", TransactionSource.Manual, null, cancelled: true);
        AddCredit(db, t, tx);
        (await Decide(db, TransactionSource.Manual, "REF-1", null))
            .Should().Be(TransactionCreateDecision.BlockedVoidHadCredits);
    }

    [Fact]
    public async Task Reference_uniqueness_is_tenant_wide_across_sources()
    {
        var t = Guid.NewGuid();
        var db = NewDb(nameof(Reference_uniqueness_is_tenant_wide_across_sources), t);
        // An active Manual transaction with this reference blocks an Excel import of the same reference.
        Seed(db, t, "REF-X", TransactionSource.Manual, null, cancelled: false);
        (await Decide(db, TransactionSource.EtlImport, "REF-X", null))
            .Should().Be(TransactionCreateDecision.SkipActiveDuplicate);
    }

    [Fact]
    public async Task External_id_duplicate_is_per_source()
    {
        var t = Guid.NewGuid();
        var db = NewDb(nameof(External_id_duplicate_is_per_source), t);
        // Active CrmSync deal D1 (reference HUBSPOT-D1, externalId D1).
        Seed(db, t, "HUBSPOT-D1", TransactionSource.CrmSync, "D1", cancelled: false);
        // Re-importing the same deal via HubSpot → skipped (active externalId for CrmSync).
        (await Decide(db, TransactionSource.CrmSync, "HUBSPOT-D1", "D1"))
            .Should().Be(TransactionCreateDecision.SkipActiveDuplicate);
        // A DIFFERENT-source row carrying externalId "D1" but a fresh reference is not blocked by externalId.
        (await Decide(db, TransactionSource.EtlImport, "EXCEL-D1", "D1"))
            .Should().Be(TransactionCreateDecision.Create);
    }

    [Fact]
    public async Task Re_import_of_a_voided_deal_creates_a_new_one_by_external_id()
    {
        var t = Guid.NewGuid();
        var db = NewDb(nameof(Re_import_of_a_voided_deal_creates_a_new_one_by_external_id), t);
        Seed(db, t, "HUBSPOT-D1", TransactionSource.CrmSync, "D1", cancelled: true);
        (await Decide(db, TransactionSource.CrmSync, "HUBSPOT-D1", "D1"))
            .Should().Be(TransactionCreateDecision.Create);
    }

    [Fact]
    public async Task Is_tenant_isolated()
    {
        const string dbName = nameof(Is_tenant_isolated);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbA = NewDb(dbName, tenantA);
        Seed(dbA, tenantA, "REF-1", TransactionSource.Manual, null, cancelled: false);
        // Tenant B doesn't see tenant A's active transaction → may create.
        var dbB = NewDb(dbName, tenantB);
        (await Decide(dbB, TransactionSource.Manual, "REF-1", null))
            .Should().Be(TransactionCreateDecision.Create);
    }

    [Fact]
    public async Task Classifies_a_large_batch_in_one_pass()
    {
        var t = Guid.NewGuid();
        var db = NewDb(nameof(Classifies_a_large_batch_in_one_pass), t);
        // 500 active references already exist; we classify 1000 candidate keys in a single batch call.
        for (var i = 0; i < 500; i++)
            Seed(db, t, $"REF-{i}", TransactionSource.EtlImport, null, cancelled: false);

        var refs = Enumerable.Range(0, 1000).Select(i => $"REF-{i}").ToList();
        var externalIds = Enumerable.Repeat<string?>(null, 1000).ToList();
        var classification = await new TransactionCreateGuard(db)
            .ClassifyAsync(TransactionSource.EtlImport, refs, externalIds, default);

        var skipped = refs.Count(r => classification.Decide(r, null) == TransactionCreateDecision.SkipActiveDuplicate);
        var creatable = refs.Count(r => classification.Decide(r, null) == TransactionCreateDecision.Create);
        skipped.Should().Be(500);   // REF-0..REF-499 already active
        creatable.Should().Be(500); // REF-500..REF-999 are new
    }
}
