using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Ledger;
using Wasnie.Application.Compensation.Handlers.Ledger;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The only human write path into the ledger. What matters here is that the handler adds nothing of
/// its own: the sign, the actor and the refusal of engine-only types all come from the sealed domain
/// factory, and the balance moves in the same save as the entry.
/// </summary>
public sealed class CreateManualLedgerAdjustmentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    private sealed record Harness(
        ApplicationDbContext Db, CreateManualLedgerAdjustmentHandler Handler, Guid TenantId, Guid PayeeId);

    private static Harness Build(string dbName, string? email = "finance@acme.com")
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.Email.Returns(email);
        currentUser.UserId.Returns((string?)null);

        var payee = Payee.Create(tenantId, "Ana Sales", "EMP-1", "ana@acme.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        db.Payees.Add(payee);
        db.SaveChanges();

        var handler = new CreateManualLedgerAdjustmentHandler(
            db, Substitute.For<IAuthorizationService>(), currentUser, tenantCtx,
            new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(), Substitute.For<IAuditService>());

        return new Harness(db, handler, tenantId, payee.Id);
    }

    [Fact]
    public async Task A_forgiveness_is_stored_positive_and_moves_the_balance_up()
    {
        var h = Build(nameof(A_forgiveness_is_stored_positive_and_moves_the_balance_up));

        var result = await h.Handler.Handle(new CreateManualLedgerAdjustmentCommand(
            h.PayeeId, "ClawbackForgivenessCredit", 600m, Eur, "Agreed with the rep."), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Amount.Should().Be(600m);
        result.Value.Origin.Should().Be("Human");
        result.Value.CreatedBy.Should().Be("finance@acme.com");

        var balance = await h.Db.PayeeBalances.SingleAsync();
        balance.Balance.Amount.Should().Be(600m);
        (await h.Db.PayeeLedgerEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_data_correction_is_stored_negative_even_though_a_positive_amount_was_sent()
    {
        var h = Build(nameof(A_data_correction_is_stored_negative_even_though_a_positive_amount_was_sent));

        var result = await h.Handler.Handle(new CreateManualLedgerAdjustmentCommand(
            h.PayeeId, "DataCorrectionDebit", 250m, Eur, "Duplicate deal removed."), default);

        result.Value!.Amount.Should().Be(-250m);
        (await h.Db.PayeeBalances.SingleAsync()).Balance.Amount.Should().Be(-250m);
    }

    [Theory]
    [InlineData("ClawbackDebit")]
    [InlineData("ClawbackAppliedCredit")]
    public async Task Engine_only_types_are_refused_through_the_API(string type)
    {
        var h = Build($"{nameof(Engine_only_types_are_refused_through_the_API)}_{type}");

        var result = await h.Handler.Handle(new CreateManualLedgerAdjustmentCommand(
            h.PayeeId, type, 100m, Eur, "reason"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("cannot be created by hand");
        (await h.Db.PayeeLedgerEntries.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_adjustment_without_a_justification_is_refused_and_writes_nothing(string justification)
    {
        var h = Build($"{nameof(An_adjustment_without_a_justification_is_refused_and_writes_nothing)}_{justification.Length}");

        var result = await h.Handler.Handle(new CreateManualLedgerAdjustmentCommand(
            h.PayeeId, "ManualBonusCredit", 100m, Eur, justification), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("justification");
        (await h.Db.PayeeLedgerEntries.CountAsync()).Should().Be(0);
        (await h.Db.PayeeBalances.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_zero_or_negative_amount_is_refused()
    {
        var h = Build(nameof(A_zero_or_negative_amount_is_refused));

        var zero = await h.Handler.Handle(new CreateManualLedgerAdjustmentCommand(
            h.PayeeId, "ManualBonusCredit", 0m, Eur, "reason"), default);
        var negative = await h.Handler.Handle(new CreateManualLedgerAdjustmentCommand(
            h.PayeeId, "ManualBonusCredit", -50m, Eur, "reason"), default);

        zero.IsSuccess.Should().BeFalse();
        negative.IsSuccess.Should().BeFalse();
        (await h.Db.PayeeLedgerEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_type_is_refused_before_any_domain_call()
    {
        var h = Build(nameof(An_unknown_type_is_refused_before_any_domain_call));

        var result = await h.Handler.Handle(new CreateManualLedgerAdjustmentCommand(
            h.PayeeId, "WhateverCredit", 100m, Eur, "reason"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Unknown adjustment type");
    }

    [Fact]
    public async Task A_second_adjustment_reuses_the_same_balance_row_and_accumulates()
    {
        var h = Build(nameof(A_second_adjustment_reuses_the_same_balance_row_and_accumulates));

        await h.Handler.Handle(new CreateManualLedgerAdjustmentCommand(
            h.PayeeId, "ManualBonusCredit", 300m, Eur, "first"), default);
        await h.Handler.Handle(new CreateManualLedgerAdjustmentCommand(
            h.PayeeId, "DataCorrectionDebit", 100m, Eur, "second"), default);

        var balances = await h.Db.PayeeBalances.ToListAsync();
        balances.Should().ContainSingle();
        balances[0].Balance.Amount.Should().Be(200m);
        (await h.Db.PayeeLedgerEntries.CountAsync()).Should().Be(2, "append-only: both entries survive");
    }

    [Fact]
    public async Task A_different_currency_opens_its_own_balance_and_never_mixes()
    {
        var h = Build(nameof(A_different_currency_opens_its_own_balance_and_never_mixes));

        await h.Handler.Handle(new CreateManualLedgerAdjustmentCommand(
            h.PayeeId, "ManualBonusCredit", 300m, Eur, "eur"), default);
        await h.Handler.Handle(new CreateManualLedgerAdjustmentCommand(
            h.PayeeId, "ManualBonusCredit", 200m, "USD", "usd"), default);

        var balances = await h.Db.PayeeBalances.ToListAsync();
        balances.Should().HaveCount(2);
        balances.Single(b => b.Currency == Eur).Balance.Should().Be(Money.Of(300m, Eur));
        balances.Single(b => b.Currency == "USD").Balance.Should().Be(Money.Of(200m, "USD"));
    }

    [Fact]
    public async Task An_adjustment_for_an_unknown_payee_is_refused()
    {
        var h = Build(nameof(An_adjustment_for_an_unknown_payee_is_refused));

        var result = await h.Handler.Handle(new CreateManualLedgerAdjustmentCommand(
            Guid.NewGuid(), "ManualBonusCredit", 100m, Eur, "reason"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Payee not found");
    }

    [Fact]
    public async Task The_entry_records_the_type_the_reporting_engine_reads()
    {
        var h = Build(nameof(The_entry_records_the_type_the_reporting_engine_reads));

        await h.Handler.Handle(new CreateManualLedgerAdjustmentCommand(
            h.PayeeId, "ClawbackForgivenessCredit", 600m, Eur, "Goodwill."), default);

        var entry = await h.Db.PayeeLedgerEntries.SingleAsync();
        entry.TransactionType.Should().Be(LedgerTransactionType.ClawbackForgivenessCredit);
        entry.Origin.Should().Be(LedgerEntryOrigin.Human);
        entry.SourceType.Should().BeNull("a human entry has no engine trigger behind it");
    }
}
