using FluentAssertions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

public sealed class CompensationTransactionTests
{
    private static readonly Guid ValidTenantId = Guid.NewGuid();
    private static readonly Guid ValidPayeeId = Guid.NewGuid();
    private static readonly Money ValidAmount = Money.Of(1500m, "EUR");
    private static readonly DateOnly ValidDate = new DateOnly(2024, 6, 15);
    private static readonly DateTimeOffset ValidNow = new DateTimeOffset(2024, 6, 16, 8, 0, 0, TimeSpan.Zero);

    private static CompensationTransaction IngestValid(string? externalId = null) =>
        CompensationTransaction.Ingest(
            ValidTenantId, "REF-001", ValidPayeeId, ValidAmount, ValidDate,
            TransactionSource.Manual, "user@test.com",
            Guid.NewGuid(), ValidNow, Guid.NewGuid(), externalId);

    // ── Factory ───────────────────────────────────────────────────────────────

    [Fact]
    public void Ingest_ValidArgs_ReturnsTransactionInPendingStatus()
    {
        var tx = IngestValid();

        tx.Status.Should().Be(CompensationTransactionStatus.Pending);
        tx.TenantId.Should().Be(ValidTenantId);
        tx.ReferenceNumber.Should().Be("REF-001");
        tx.Amount.Should().Be(ValidAmount);
        tx.TransactionDate.Should().Be(ValidDate);
    }

    [Fact]
    public void Ingest_ValidArgs_RaisesTransactionIngestedEvent()
    {
        var tx = IngestValid();

        tx.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransactionIngestedEvent>();
    }

    [Fact]
    public void Ingest_ValidArgs_WithExternalId_SetsExternalId()
    {
        var tx = IngestValid(externalId: "CRM-XYZ-001");

        tx.ExternalId.Should().Be("CRM-XYZ-001");
    }

    [Fact]
    public void Ingest_ValidArgs_WithNullExternalId_ExternalIdIsNull()
    {
        var tx = IngestValid(externalId: null);

        tx.ExternalId.Should().BeNull();
    }

    [Fact]
    public void Ingest_EmptyTenantId_ThrowsDomainException()
    {
        var act = () => CompensationTransaction.Ingest(
            Guid.Empty, "REF-001", ValidPayeeId, ValidAmount, ValidDate,
            TransactionSource.Manual, "user@test.com",
            Guid.NewGuid(), ValidNow, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*TenantId*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ingest_NullOrBlankReferenceNumber_ThrowsDomainException(string? refNumber)
    {
        var act = () => CompensationTransaction.Ingest(
            ValidTenantId, refNumber!, ValidPayeeId, ValidAmount, ValidDate,
            TransactionSource.Manual, "user@test.com",
            Guid.NewGuid(), ValidNow, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*Reference number*");
    }

    [Fact]
    public void Ingest_EmptyPayeeId_ThrowsDomainException()
    {
        var act = () => CompensationTransaction.Ingest(
            ValidTenantId, "REF-001", Guid.Empty, ValidAmount, ValidDate,
            TransactionSource.Manual, "user@test.com",
            Guid.NewGuid(), ValidNow, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*PayeeId*");
    }

    [Fact]
    public void Ingest_TransactionDateBeforeMinimum_ThrowsDomainException()
    {
        var ancientDate = new DateOnly(1999, 12, 31);

        var act = () => CompensationTransaction.Ingest(
            ValidTenantId, "REF-001", ValidPayeeId, ValidAmount, ancientDate,
            TransactionSource.Manual, "user@test.com",
            Guid.NewGuid(), ValidNow, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*2000-01-01*");
    }

    [Fact]
    public void Ingest_TransactionDateAtMinimum_Succeeds()
    {
        var minDate = new DateOnly(2000, 1, 1);

        var act = () => CompensationTransaction.Ingest(
            ValidTenantId, "REF-001", ValidPayeeId, ValidAmount, minDate,
            TransactionSource.Manual, "user@test.com",
            Guid.NewGuid(), ValidNow, Guid.NewGuid());

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Ingest_NullOrEmptyIngestedBy_ThrowsDomainException(string? ingestedBy)
    {
        var act = () => CompensationTransaction.Ingest(
            ValidTenantId, "REF-001", ValidPayeeId, ValidAmount, ValidDate,
            TransactionSource.Manual, ingestedBy!,
            Guid.NewGuid(), ValidNow, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*IngestedBy*");
    }

    // ── MarkEligible ──────────────────────────────────────────────────────────

    [Fact]
    public void MarkEligible_WhenPending_TransitionsToEligible()
    {
        var tx = IngestValid();

        tx.MarkEligible("validator", ValidNow, Guid.NewGuid());

        tx.Status.Should().Be(CompensationTransactionStatus.Eligible);
    }

    [Fact]
    public void MarkEligible_WhenPending_RaisesTransactionMarkedEligibleEvent()
    {
        var tx = IngestValid();
        tx.ClearDomainEvents();

        tx.MarkEligible("validator", ValidNow, Guid.NewGuid());

        tx.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransactionMarkedEligibleEvent>();
    }

    [Fact]
    public void MarkEligible_WhenAlreadyEligible_ThrowsDomainException()
    {
        var tx = IngestValid();
        tx.MarkEligible("validator", ValidNow, Guid.NewGuid());
        tx.ClearDomainEvents();

        var act = () => tx.MarkEligible("validator", ValidNow, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*Pending*");
    }

    [Fact]
    public void MarkEligible_WhenCancelled_ThrowsDomainException()
    {
        var tx = IngestValid();
        tx.Cancel("user", ValidNow, Guid.NewGuid());
        tx.ClearDomainEvents();

        var act = () => tx.MarkEligible("validator", ValidNow, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*Pending*");
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_WhenPending_TransitionsToCancelled()
    {
        var tx = IngestValid();
        tx.ClearDomainEvents();

        tx.Cancel("user", ValidNow, Guid.NewGuid());

        tx.Status.Should().Be(CompensationTransactionStatus.Cancelled);
    }

    [Fact]
    public void Cancel_WhenPending_RaisesTransactionCancelledEvent()
    {
        var tx = IngestValid();
        tx.ClearDomainEvents();

        tx.Cancel("user", ValidNow, Guid.NewGuid());

        tx.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransactionCancelledEvent>();
    }

    [Fact]
    public void Cancel_WhenEligible_TransitionsToCancelled()
    {
        var tx = IngestValid();
        tx.MarkEligible("validator", ValidNow, Guid.NewGuid());
        tx.ClearDomainEvents();

        tx.Cancel("user", ValidNow, Guid.NewGuid());

        tx.Status.Should().Be(CompensationTransactionStatus.Cancelled);
    }

    [Fact]
    public void Cancel_WhenEligible_RaisesTransactionCancelledEvent()
    {
        var tx = IngestValid();
        tx.MarkEligible("validator", ValidNow, Guid.NewGuid());
        tx.ClearDomainEvents();

        tx.Cancel("user", ValidNow, Guid.NewGuid());

        tx.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TransactionCancelledEvent>();
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_ThrowsDomainException()
    {
        var tx = IngestValid();
        tx.Cancel("user", ValidNow, Guid.NewGuid());
        tx.ClearDomainEvents();

        var act = () => tx.Cancel("user", ValidNow, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*already cancelled*");
    }

    // ── Phase 3 stubs ─────────────────────────────────────────────────────────

    [Fact]
    public void MarkCalculated_AlwaysThrowsNotSupportedException()
    {
        var tx = IngestValid();

        var act = () => tx.MarkCalculated("engine", ValidNow, Guid.NewGuid());

        act.Should().Throw<NotSupportedException>().WithMessage("*Phase 3*");
    }

    [Fact]
    public void MarkPaid_AlwaysThrowsNotSupportedException()
    {
        var tx = IngestValid();

        var act = () => tx.MarkPaid("payroll", ValidNow, Guid.NewGuid());

        act.Should().Throw<NotSupportedException>().WithMessage("*Phase 3*");
    }

    // ── §5b.7 regression guard: every state-change raises an event ─────────────

    [Fact]
    public void MarkEligible_OnSuccess_RaisesExactlyOneDomainEvent()
    {
        var tx = IngestValid();
        tx.ClearDomainEvents();

        tx.MarkEligible("validator", ValidNow, Guid.NewGuid());

        tx.DomainEvents.Should().HaveCount(1);
    }

    [Fact]
    public void Cancel_OnSuccess_RaisesExactlyOneDomainEvent()
    {
        var tx = IngestValid();
        tx.ClearDomainEvents();

        tx.Cancel("user", ValidNow, Guid.NewGuid());

        tx.DomainEvents.Should().HaveCount(1);
    }

    [Fact]
    public void Ingest_OnSuccess_RaisesExactlyOneDomainEvent()
    {
        var tx = IngestValid();

        tx.DomainEvents.Should().HaveCount(1);
    }
}
