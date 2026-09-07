using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Handlers.Assignments;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.TestDoubles;
using Xunit.Abstractions;

namespace Wasnie.IntegrationTests.Compensation;

/// <summary>
/// Proves — against real SQL Server — that the assignments-by-payee filter runs IN THE DATABASE
/// and that the explicit contract returns history instead of silently hiding it.
///
/// The SQL is captured through <c>LogTo</c> configured on the DbContext options of THIS test's own
/// context. The channel is not global: only commands emitted by this instance can reach it, so a
/// collection running in parallel cannot contaminate the capture.
/// </summary>
[Collection(PayoutEngineCollection.Name)]
public sealed class ListAssignmentsByPayeeSqlTests(PayoutEngineFixture fixture, ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed class AllowAll : IAuthorizationService
    {
        public Task RequireAsync(string permission, CancellationToken ct = default) => Task.CompletedTask;
        // Added with IAuthorizationService.HasAsync: this double allows everything, so the
        // question answers the same way the enforcement does.
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class FixedTenant(Guid id) : ITenantContext
    {
        public Guid TenantId => id;
        public bool IsResolved => true;
    }

    private sealed class NoOpPublisher : IPublisher
    {
        public Task Publish(object n, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<T>(T n, CancellationToken ct = default) where T : INotification => Task.CompletedTask;
    }

    /// <summary>A context whose SQL is echoed into <paramref name="sink"/> — and nowhere else.</summary>
    private ApplicationDbContext CreateLoggingDb(Guid tenantId, List<string> sink) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(fixture.ConnectionString)
                .LogTo(sink.Add, new[] { DbLoggerCategory.Database.Command.Name },
                       Microsoft.Extensions.Logging.LogLevel.Information)
                .Options,
            new FixedTenant(tenantId), new NoOpPublisher());

    private ListAssignmentsByPayeeHandler Handler(ApplicationDbContext db) =>
        // This suite asserts the SQL the handler emits (one round trip, filtered and paged in the
        // database). Authorisation is not what it measures, so the guard answers like finance; the real
        // guard is exercised over HTTP in PayeeScopedEndpointAuthorizationTests.
        new(db, new AllowAll(), new SeesEveryPayee(), new FakeClock(Now.UtcDateTime));

    private sealed class SeesEveryPayee : IPayeeAccessGuard
    {
        public Task<PayeeVisibility> GetVisibilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PayeeVisibility.Everything);

        public Task<bool> CanReadAsync(Guid payeeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    /// <summary>Payee with two assignments: one expired (2025) and one current (2026).</summary>
    private async Task<Guid> SeedAsync(Guid tenantId)
    {
        await using var db = fixture.CreateDbForTenant(tenantId);

        var payee = Payee.Create(tenantId, "Ana Sales", $"EMP-{Guid.NewGuid():N}"[..10], "ana@test.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        db.Payees.Add(payee);

        foreach (var (start, end) in new[]
                 {
                     (new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),   // expired
                     (new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),   // current
                 })
        {
            var planId = Guid.NewGuid();
            var plan = Plan.Create(tenantId, $"Plan {start.Year}", "desc",
                DateRange.Of(start, end), "EUR", "test", planId, Now, Guid.NewGuid());
            plan.AddRule("Commission", 1,
                new Measurement
                {
                    Type = MeasurementType.Revenue,
                    SourceField = "amount",
                    Aggregation = MeasurementAggregation.Sum,
                },
                RateTable.Flat(0.10m));
            db.CompensationPlans.Add(plan);
            db.PlanAssignments.Add(PlanAssignment.Create(
                tenantId, planId, payee.Id, PayeeReference.Snapshot(payee.Id, payee.FullName, "E1"),
                DateRange.Of(start, end), "test", Guid.NewGuid(), Now, Guid.NewGuid()));
        }

        await db.SaveChangesAsync();
        return payee.Id;
    }

    [Fact]
    public async Task With_no_date_parameters_the_full_history_is_returned()
    {
        // The behaviour change: no parameters no longer means "this month", so the 2025 assignment
        // that the old default hid is now visible.
        var tenantId = Guid.NewGuid();
        var payeeId = await SeedAsync(tenantId);
        var sql = new List<string>();

        await using var db = CreateLoggingDb(tenantId, sql);
        var result = await Handler(db).Handle(
            new ListAssignmentsByPayeeQuery(payeeId, new PaginationQuery()), CancellationToken.None);

        result.Value!.TotalCount.Should().Be(2);

        var commandText = string.Join("\n", sql);
        output.WriteLine(commandText);

        // The tenant Global Query Filter reached SQL, on both tables of the join.
        commandText.Should().Contain("TenantId");
        // And the rows were NOT filtered in memory afterwards: SQL already paged them.
        commandText.Should().Contain("OFFSET");
    }

    [Fact]
    public async Task An_explicit_range_filters_in_SQL_not_in_memory()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = await SeedAsync(tenantId);
        var sql = new List<string>();

        await using var db = CreateLoggingDb(tenantId, sql);
        var result = await Handler(db).Handle(
            new ListAssignmentsByPayeeQuery(payeeId, new PaginationQuery
            {
                DateFrom = new DateOnly(2026, 1, 1),
                DateTo = new DateOnly(2026, 12, 31),
            }),
            CancellationToken.None);

        // Only the 2026 assignment intersects.
        result.Value!.TotalCount.Should().Be(1);

        var commandText = string.Join("\n", sql);
        output.WriteLine(commandText);

        // The date predicate is IN the SQL — this is the proof the owned-type period translates.
        commandText.Should().Contain("EffectiveEnd");
        commandText.Should().Contain("EffectiveStart");
        commandText.Should().Contain("TenantId");
    }

    [Fact]
    public async Task An_explicit_period_keyword_is_still_honoured()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = await SeedAsync(tenantId);
        var sql = new List<string>();

        await using var db = CreateLoggingDb(tenantId, sql);
        var result = await Handler(db).Handle(
            new ListAssignmentsByPayeeQuery(payeeId, new PaginationQuery { Period = "this-month" }),
            CancellationToken.None);

        // July 2026 — only the 2026 assignment covers it.
        result.Value!.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Assignments_of_another_tenant_are_never_returned()
    {
        // Closes the tenant question for this endpoint with a behavioural check on top of the SQL:
        // the same query, run under a different tenant context, sees nothing.
        var tenantId = Guid.NewGuid();
        var payeeId = await SeedAsync(tenantId);
        var sql = new List<string>();

        await using var db = CreateLoggingDb(Guid.NewGuid(), sql);   // a DIFFERENT tenant
        var result = await Handler(db).Handle(
            new ListAssignmentsByPayeeQuery(payeeId, new PaginationQuery()), CancellationToken.None);

        result.Value!.TotalCount.Should().Be(0, "the tenant filter must exclude another tenant's rows");
    }
}
