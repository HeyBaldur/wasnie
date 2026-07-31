using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Quotas;
using Wasnie.Application.Compensation.Handlers.Quotas;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The all-or-nothing property of bulk quota creation, tested where it can actually be falsified.
///
/// ★ WHY THIS FILE EXISTS, and it matters for reading the HTTP tests: every rule that decides whether
/// a quota may exist (period inside the plan, currency matching the plan, notes length) reads inputs
/// that are the SAME for the whole batch. The only per-payee input is the payee id, and no rule reads
/// it. So through the API, a batch fails entirely or succeeds entirely — the "18 created, 2 failed"
/// state is unreachable today, and an integration test claiming to rule it out would be theatre: it
/// would pass just as happily against a handler that writes first and validates after.
///
/// What IS falsifiable, and what these tests pin, is the ORDER: nothing is written unless every quota
/// validated. That is the property that keeps the guarantee true the day a per-payee rule appears.
/// </summary>
public sealed class BulkCreateQuotasHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Counts SaveChanges so a test can assert the write never HAPPENED, not merely that it wrote
    /// nothing. An interceptor rather than a subclass because ApplicationDbContext is sealed — and
    /// better anyway: it observes the real context instead of a test-only variant of it.
    /// </summary>
    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Calls { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Calls++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed record Harness(
        ApplicationDbContext Db, BulkCreateQuotasHandler Handler, Guid TenantId, Plan Plan, SaveCounter Saves);

    private static Harness Build(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var saves = new SaveCounter();
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .AddInterceptors(saves)
                .Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("admin");

        var auth = Substitute.For<IAuthorizationService>();

        var plan = Plan.Create(
            tenantId, "EU Accelerator", "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "EUR", "admin", Guid.NewGuid(), Now, Guid.NewGuid());
        plan.AddRule(
            "Base Commission", sortOrder: 1,
            measurement: new Measurement
            {
                Type = MeasurementType.Revenue,
                SourceField = "amount",
                Aggregation = MeasurementAggregation.Sum,
            },
            rateTable: RateTable.Flat(0.05m));
        db.CompensationPlans.Add(plan);
        db.SaveChanges();

        var handler = new BulkCreateQuotasHandler(
            db, tenantCtx, currentUser, new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(), auth);

        // Only the handler's own writes are interesting; the seeding above is not one of them.
        return new Harness(db, handler, tenantId, plan, saves);
    }

    private static BulkCreateQuotasCommand Command(
        Harness h, IReadOnlyList<Guid> payeeIds, DateOnly? start = null, DateOnly? end = null, string currency = "EUR") =>
        new(payeeIds, h.Plan.Id, QuotaMeasurementType.Revenue, 10_000m, currency,
            start ?? new DateOnly(2026, 4, 1), end ?? new DateOnly(2026, 6, 30));

    [Fact]
    public async Task A_valid_batch_writes_every_quota_in_ONE_SaveChanges()
    {
        var h = Build(nameof(A_valid_batch_writes_every_quota_in_ONE_SaveChanges));
        var payees = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var savesBefore = h.Saves.Calls;

        var result = await h.Handler.Handle(Command(h, payees), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Failures.Should().BeEmpty();
        result.Value.Created.Should().HaveCount(3);

        // ★ ONE write for the whole batch. EF runs a SaveChanges inside a single transaction, so this
        // is what makes the insert of N rows atomic. A handler saving once per payee would pass every
        // other assertion in this file and still leave 18-of-20 behind on the first bad row.
        (h.Saves.Calls - savesBefore).Should().Be(1);

        var stored = await h.Db.Quotas.IgnoreQueryFilters().ToListAsync();
        stored.Should().HaveCount(3);
        stored.Select(q => q.PayeeId).Should().BeEquivalentTo(payees);
        // Identical in everything but the payee — that is what "the same quota for all" means.
        stored.Should().OnlyContain(q => q.Amount.Amount == 10_000m && q.Amount.Currency == "EUR");
        stored.Should().OnlyContain(q => q.PlanId == h.Plan.Id);
    }

    [Fact]
    public async Task A_batch_with_a_failure_never_reaches_the_write_at_all()
    {
        // ★ THE ATOMICITY TEST. Asserting "no rows were written" would pass against a handler that
        // writes first and validates after, because a failing batch produces nothing to write anyway.
        // Asserting that SaveChanges was never CALLED is what actually pins the ordering.
        var h = Build(nameof(A_batch_with_a_failure_never_reaches_the_write_at_all));
        var savesBefore = h.Saves.Calls;

        // A period outside the plan — the same rule the single create enforces.
        var result = await h.Handler.Handle(
            Command(h, [Guid.NewGuid(), Guid.NewGuid()],
                start: new DateOnly(2025, 1, 1), end: new DateOnly(2025, 3, 31)),
            CancellationToken.None);

        result.Value!.Failures.Should().HaveCount(2);
        result.Value.Created.Should().BeEmpty();
        (h.Saves.Calls - savesBefore).Should().Be(0, "a refused batch must not touch the database");
        (await h.Db.Quotas.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Each_quota_gets_its_OWN_value_objects()
    {
        // The trap that broke the first working version: `Money` and `DateRange` are EF owned types,
        // so handing the same instance to several quotas makes the tracker treat one owned entity as
        // having several owners — and every insert after the first writes NULL into a NOT NULL column.
        var h = Build(nameof(Each_quota_gets_its_OWN_value_objects));

        var result = await h.Handler.Handle(
            Command(h, [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]), CancellationToken.None);

        result.Value!.Created.Should().HaveCount(3);

        var stored = await h.Db.Quotas.IgnoreQueryFilters().ToListAsync();

        // REFERENCE distinctness, not value distinctness: Money and DateRange compare by value, so the
        // three quotas are supposed to be equal — what must not happen is three quotas pointing at ONE
        // object, which is what the change tracker chokes on.
        stored.Select(q => (object)q.Amount).Distinct(ReferenceEqualityComparer.Instance)
            .Should().HaveCount(3, "each quota owns its own Money instance");
        stored.Select(q => (object)q.Period).Distinct(ReferenceEqualityComparer.Instance)
            .Should().HaveCount(3, "each quota owns its own DateRange instance");
        stored.Should().OnlyContain(q => q.Amount.Amount == 10_000m, "…while carrying identical values");
    }

    [Fact]
    public async Task The_batch_refuses_exactly_what_the_single_create_refuses()
    {
        // Parity is structural — both call QuotaBuilder — but stated here so a future "quick fix" that
        // inlines validation into one of the two paths breaks a test that explains why it is wrong.
        var h = Build(nameof(The_batch_refuses_exactly_what_the_single_create_refuses));

        var wrongCurrency = await h.Handler.Handle(
            Command(h, [Guid.NewGuid()], currency: "PLN"), CancellationToken.None);
        wrongCurrency.Value!.Failures.Should().ContainSingle()
            .Which.Reason.Should().Contain("currency");

        var outsidePlan = await h.Handler.Handle(
            Command(h, [Guid.NewGuid()], start: new DateOnly(2025, 1, 1), end: new DateOnly(2025, 3, 31)),
            CancellationToken.None);
        outsidePlan.Value!.Failures.Should().ContainSingle()
            .Which.Reason.Should().Contain("plan");
    }

    [Fact]
    public async Task An_unknown_plan_is_refused_before_anything_else_happens()
    {
        var h = Build(nameof(An_unknown_plan_is_refused_before_anything_else_happens));
        var savesBefore = h.Saves.Calls;

        var result = await h.Handler.Handle(
            new BulkCreateQuotasCommand(
                [Guid.NewGuid()], Guid.NewGuid(), QuotaMeasurementType.Revenue, 10_000m, "EUR",
                new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Plan not found");
        (h.Saves.Calls - savesBefore).Should().Be(0);
    }

    [Fact]
    public async Task Creating_quotas_requires_the_same_permission_as_creating_one()
    {
        // Creating twenty is not a different authority from creating one.
        var h = Build(nameof(Creating_quotas_requires_the_same_permission_as_creating_one));
        var auth = Substitute.For<IAuthorizationService>();
        var handler = new BulkCreateQuotasHandler(
            h.Db, Substitute.For<ITenantContext>(), Substitute.For<ICurrentUserService>(),
            new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(), auth);

        await handler.Handle(Command(h, [Guid.NewGuid()]), CancellationToken.None);

        await auth.Received(1).RequireAsync(Permission.QuotasSet, Arg.Any<CancellationToken>());
    }
}
