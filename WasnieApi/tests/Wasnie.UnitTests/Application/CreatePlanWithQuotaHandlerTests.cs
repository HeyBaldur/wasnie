using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.Commands.Quotas;
using Wasnie.Application.Compensation.Handlers.Plans;
using Wasnie.Application.Compensation.Handlers.Quotas;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Plan + quota as ONE write, tested where it can actually be falsified.
///
/// The property under test is not "both rows exist" — it is that the plan cannot outlive a failed
/// quota. A plan carrying an accelerator with no quota measures attainment against nothing and pays
/// €0 without raising anything, so "plan created, quota rejected" is the single outcome this command
/// exists to make unreachable.
/// </summary>
public sealed class CreatePlanWithQuotaHandlerTests
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
        ApplicationDbContext Db,
        CreatePlanWithQuotaHandler Handler,
        Guid TenantId,
        FakeGuidGenerator Guids,
        SaveCounter Saves,
        IAuthorizationService Auth);

    /// <param name="testName">
    /// Namespaced into the database name below. InMemory databases are shared per NAME across the
    /// whole test process, and a bare nameof() collides with the identically-named test in
    /// BulkCreateQuotasHandlerTests — two tests then quietly share rows and one of them fails.
    /// </param>
    private static Harness Build(string testName)
    {
        var dbName = $"{nameof(CreatePlanWithQuotaHandlerTests)}.{testName}";
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
        var guids = new FakeGuidGenerator();

        var handler = new CreatePlanWithQuotaHandler(
            db, tenantCtx, currentUser, new FakeClock(Now.UtcDateTime), guids,
            Substitute.For<IAuditService>(), auth, Substitute.For<ITierLimitChecker>());

        return new Harness(db, handler, tenantId, guids, saves, auth);
    }

    private static PlanQuotaSpec Quota(
        Guid? payeeId = null, DateOnly? start = null, DateOnly? end = null, string currency = "EUR") =>
        new(payeeId ?? Guid.NewGuid(), QuotaMeasurementType.Revenue, 10_000m, currency,
            start ?? new DateOnly(2026, 4, 1), end ?? new DateOnly(2026, 6, 30));

    private static CreatePlanWithQuotaCommand Command(params PlanQuotaSpec[] quotas) =>
        new("EU Accelerator", "Q2 targets", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "EUR", quotas);

    // ── 1. The happy path, and it is ONE write ────────────────────────────────

    [Fact]
    public async Task Plan_and_quotas_are_created_in_ONE_SaveChanges()
    {
        var h = Build(nameof(Plan_and_quotas_are_created_in_ONE_SaveChanges));
        var payeeA = Guid.NewGuid();
        var payeeB = Guid.NewGuid();

        var result = await h.Handler.Handle(Command(Quota(payeeA), Quota(payeeB)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsSuccess.Should().BeTrue();
        result.Value.Failures.Should().BeEmpty();
        result.Value.Quotas.Should().HaveCount(2);

        // ★ ONE write for plan AND quotas. EF runs a SaveChanges inside a single transaction, so this
        // is what makes the pair atomic. A handler saving the plan first — the natural way to write
        // this if the id came from the database — would pass every other assertion in this file and
        // still leave a quota-less plan behind on the first bad quota.
        h.Saves.Calls.Should().Be(1);

        var storedPlan = await h.Db.CompensationPlans.IgnoreQueryFilters().SingleAsync();
        var storedQuotas = await h.Db.Quotas.IgnoreQueryFilters().ToListAsync();

        storedPlan.Name.Should().Be("EU Accelerator");
        storedQuotas.Should().HaveCount(2);
        storedQuotas.Select(q => q.PayeeId).Should().BeEquivalentTo(new[] { payeeA, payeeB });
    }

    // ── 2. THE ATOMICITY TEST ─────────────────────────────────────────────────

    [Fact]
    public async Task An_invalid_quota_takes_the_PLAN_down_with_it_and_nothing_is_written()
    {
        // ★ Asserting "no rows were written" would pass against a handler that writes the plan first
        // and validates the quota after — the plan would be there, but so would a passing test if it
        // only counted quotas. This asserts SaveChanges was never CALLED, which pins the ordering,
        // AND that the plan table is empty, which pins the thing that actually hurts.
        var h = Build(nameof(An_invalid_quota_takes_the_PLAN_down_with_it_and_nothing_is_written));

        // A quota period outside the plan's effective period — the same rule the single create enforces.
        var result = await h.Handler.Handle(
            Command(
                Quota(),
                Quota(start: new DateOnly(2025, 1, 1), end: new DateOnly(2025, 3, 31))),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsSuccess.Should().BeFalse("the request carried a quota the domain refuses");
        result.Value.Plan.Should().BeNull();
        result.Value.Quotas.Should().BeEmpty();
        result.Value.Failures.Should().ContainSingle();
        result.Value.Failures[0].Index.Should().Be(1, "the failure points at the offending position");

        h.Saves.Calls.Should().Be(0, "a refused request must not touch the database");
        (await h.Db.CompensationPlans.IgnoreQueryFilters().CountAsync())
            .Should().Be(0, "★ the plan must NOT survive its quota — that is the whole point");
        (await h.Db.Quotas.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    // ── 3. Parity with the single-quota path ──────────────────────────────────

    [Fact]
    public async Task It_refuses_exactly_what_the_single_quota_create_refuses()
    {
        // Parity is structural — both call QuotaBuilder — but pinned here so a future "quick fix" that
        // inlines validation into one of the paths breaks a test that explains why it is wrong.
        // Same inputs, both paths, same rejection.
        var h = Build(nameof(It_refuses_exactly_what_the_single_quota_create_refuses));

        // The composite path: a quota in PLN against a EUR plan.
        var composite = await h.Handler.Handle(Command(Quota(currency: "PLN")), CancellationToken.None);
        composite.Value!.Failures.Should().ContainSingle()
            .Which.Reason.Should().Contain("currency");

        // The single path, against a plan that now exists with the same shape. Seeded directly so the
        // comparison is between the two validation paths, not between two ways of making a plan.
        var plan = Wasnie.Domain.Compensation.Plans.Plan.Create(
            h.TenantId, "EU Accelerator", "Q2 targets",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "EUR", "admin", Guid.NewGuid(), Now, Guid.NewGuid());
        h.Db.CompensationPlans.Add(plan);
        await h.Db.SaveChangesAsync();

        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(h.TenantId);
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("admin");
        var single = new CreateQuotaHandler(
            h.Db, tenantCtx, currentUser, new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            Substitute.For<IAuthorizationService>());

        var singleResult = await single.Handle(
            new CreateQuotaCommand(
                Guid.NewGuid(), plan.Id, QuotaMeasurementType.Revenue, 10_000m, "PLN",
                new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30), null),
            CancellationToken.None);

        singleResult.IsSuccess.Should().BeFalse();
        singleResult.Error.Should().Contain("currency");

        // Same rule, same wording, two paths — because it is literally the same call.
        composite.Value.Failures[0].Reason.Should().Be(singleResult.Error);
    }

    // ── 4. The quota hangs off the plan's real, in-memory-generated id ─────────

    [Fact]
    public async Task The_quota_points_at_the_plan_id_generated_in_memory()
    {
        // The id comes from IGuidGenerator BEFORE any write, which is what allows a single SaveChanges.
        // Pinning the exact value rules out the failure this design avoids: a quota written with an
        // empty/placeholder PlanId that no plan ever claims.
        var h = Build(nameof(The_quota_points_at_the_plan_id_generated_in_memory));
        var plannedPlanId = Guid.NewGuid();
        var plannedEventId = Guid.NewGuid();
        var plannedQuotaId = Guid.NewGuid();
        h.Guids.Enqueue(plannedPlanId, plannedEventId, plannedQuotaId);

        var result = await h.Handler.Handle(Command(Quota()), CancellationToken.None);

        result.Value!.Plan!.Id.Should().Be(plannedPlanId);

        var storedQuota = await h.Db.Quotas.IgnoreQueryFilters().SingleAsync();
        storedQuota.PlanId.Should().Be(plannedPlanId);
        storedQuota.PlanId.Should().NotBe(Guid.Empty);
        storedQuota.Id.Should().Be(plannedQuotaId);

        // And the plan it points at is really there, in the same write.
        (await h.Db.CompensationPlans.IgnoreQueryFilters().AnyAsync(p => p.Id == plannedQuotaId))
            .Should().BeFalse();
        (await h.Db.CompensationPlans.IgnoreQueryFilters().SingleAsync()).Id.Should().Be(plannedPlanId);
    }

    // ── Supporting guarantees ─────────────────────────────────────────────────

    [Fact]
    public async Task Each_quota_gets_its_OWN_value_objects()
    {
        // Money and DateRange are EF owned types: handing one instance to several quotas makes the
        // tracker treat one owned entity as having several owners, and every insert after the first
        // writes NULL into a NOT NULL column. QuotaBuilder copies them; this pins that it still does
        // when the caller is this handler.
        var h = Build(nameof(Each_quota_gets_its_OWN_value_objects));

        await h.Handler.Handle(Command(Quota(), Quota(), Quota()), CancellationToken.None);

        var stored = await h.Db.Quotas.IgnoreQueryFilters().ToListAsync();
        stored.Should().HaveCount(3);
        stored.Select(q => (object)q.Amount).Distinct(ReferenceEqualityComparer.Instance)
            .Should().HaveCount(3, "each quota owns its own Money instance");
        stored.Select(q => (object)q.Period).Distinct(ReferenceEqualityComparer.Instance)
            .Should().HaveCount(3, "each quota owns its own DateRange instance");
    }

    [Fact]
    public async Task It_requires_the_authority_to_create_BOTH_entities()
    {
        // Doing it in one request is not a way to need less permission than doing it in two.
        var h = Build(nameof(It_requires_the_authority_to_create_BOTH_entities));

        await h.Handler.Handle(Command(Quota()), CancellationToken.None);

        await h.Auth.Received(1).RequireAsync(Permission.PlansCreate, Arg.Any<CancellationToken>());
        await h.Auth.Received(1).RequireAsync(Permission.QuotasSet, Arg.Any<CancellationToken>());
    }
}
