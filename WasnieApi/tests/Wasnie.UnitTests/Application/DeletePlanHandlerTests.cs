using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.Handlers.Plans;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.Reconciliation;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-69 — permanently deleting a Draft plan, and only when nothing points at it.
///
/// ★ THE MONEY GATE. Only PlanRules has a foreign key to the plan; every dependency below is a bare GUID,
/// so without the check the database would delete the plan and orphan credits and payouts silently.
/// </summary>
public sealed class DeletePlanHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    // A property, not a field: DateRange is an EF OWNED type, and one instance shared by several owners breaks
    // the change tracker (same trap QuotaBuilder documents). Every use gets its own.
    private static DateRange Year => DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _auth;

    public DeletePlanHandlerTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        tenantCtx.IsResolved.Returns(true);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        _auth = Substitute.For<IAuthorizationService>();
        _auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    public void Dispose() => _db.Dispose();

    // ── Seeding ─────────────────────────────────────────────────────────────────────

    private Plan SeedDraft(string name = "Draft plan", int rules = 2, bool activate = false)
    {
        var plan = Plan.Create(TenantId, name, "desc", Year, "EUR", "system", Guid.NewGuid(), Now, Guid.NewGuid());
        for (var i = 1; i <= rules; i++)
            plan.AddRule($"Rule {i}", i,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.05m));
        if (activate)
            plan.Activate("system", Now, Guid.NewGuid());
        _db.CompensationPlans.Add(plan);
        _db.SaveChanges();
        return plan;
    }

    private void SeedDependency(Plan plan, PlanDeletionBlocker kind)
    {
        var payeeId = Guid.NewGuid();
        switch (kind)
        {
            case PlanDeletionBlocker.Assignments:
                _db.PlanAssignments.Add(PlanAssignment.Create(TenantId, plan.Id, payeeId,
                    PayeeReference.Snapshot(payeeId, "Payee", "EMP-1"), Year, "system", Guid.NewGuid(), Now, Guid.NewGuid()));
                break;

            case PlanDeletionBlocker.Quotas:
                _db.Quotas.Add(Quota.Create(TenantId, payeeId, plan.Id, Money.Of(50_000m, "EUR"), Year,
                    QuotaMeasurementType.Revenue, "system", Guid.NewGuid(), Now, planCurrency: "EUR"));
                break;

            case PlanDeletionBlocker.Credits:
                var ruleId = plan.Rules.First().Id;
                _db.Credits.Add(Credit.Allocate(TenantId, Guid.NewGuid(), payeeId, plan.Id, ruleId,
                    RuleSnapshot.Freeze(ruleId, plan.Id, 1, "Rule 1", RateTable.Flat(0.05m), Trigger.Always(), Now),
                    Money.Of(1_000m, "EUR"), Money.Of(1_000m, "EUR"),
                    Percentage.FromPercent(100), CreditRole.Primary, "seed", Guid.NewGuid(), Now, Guid.NewGuid()));
                break;

            case PlanDeletionBlocker.Payouts:
                _db.CompensationPayouts.Add(CompensationPayout.Calculate(TenantId, payeeId, plan.Id,
                    PayeeReference.Snapshot(payeeId, "Payee", "EMP-1"), Year,
                    [], "EUR", "system", Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid));
                break;

            case PlanDeletionBlocker.LedgerEntries:
                _db.PayeeLedgerEntries.Add(PayeeLedgerEntry.CreateSystemEntry(TenantId, payeeId,
                    LedgerTransactionType.ClawbackDebit, Money.Of(100m, "EUR"), "Deal churned.",
                    LedgerSourceType.DealChurn, "system", Guid.NewGuid(), Now, Guid.NewGuid(), sourcePlanId: plan.Id));
                break;

            case PlanDeletionBlocker.ReconciliationClosures:
                _db.ReconciliationClosures.Add(ReconciliationClosure.Create(Guid.NewGuid(), TenantId, 0, plan.Id,
                    "PlanWithoutLiveRules", Now, null, "Left as it stands.", null, Now, "user-1", "user@test"));
                break;
        }
        _db.SaveChanges();
    }

    private Task<Wasnie.Domain.Common.Results.Result> Delete(DeletePlanCommand command) =>
        new DeletePlanHandler(_db, _auth).Handle(command, CancellationToken.None);

    // ── Deletes ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CleanDraft_WithRules_IsDeleted_TogetherWithItsRules()
    {
        // Rules used to block the delete ("Draft plans with active rules cannot be deleted"). They are
        // configuration, not activity: a clean Draft with rules is exactly the dead draft KAN-69 removes.
        var plan = SeedDraft(rules: 2);

        var result = await Delete(new DeletePlanCommand(plan.Id));

        result.IsSuccess.Should().BeTrue();
        _db.CompensationPlans.IgnoreQueryFilters().Any(p => p.Id == plan.Id).Should().BeFalse();
        _db.Set<Rule>().IgnoreQueryFilters().Any(r => r.PlanId == plan.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_RecordsWhatWasDeleted_ForTheAuditRow()
    {
        var plan = SeedDraft(name: "Q3 test draft", rules: 3);
        var command = new DeletePlanCommand(plan.Id);

        await Delete(command);

        command.AuditAction.Should().Be(AuditActions.PlanDeleted);
        command.AuditResourceId.Should().Be(plan.Id.ToString());
        command.AuditDisplayName.Should().Be("Q3 test draft");
        command.AuditMetadata.Should().Contain(new Dictionary<string, string>
        {
            ["name"] = "Q3 test draft",
            ["version"] = "1",
            ["currency"] = "EUR",
            ["effectiveStart"] = "2026-01-01",
            ["effectiveEnd"] = "2026-12-31",
            ["ruleCount"] = "3",
        });
    }

    [Fact]
    public void DeletePlanCommand_IsMoneyCritical_SoTheDeleteAndItsAuditRowCommitTogether()
    {
        // Through AuditBehavior's money path: if the audit write fails, the delete rolls back. A deleted
        // plan whose deletion left no trace is exactly what the ticket forbids.
        typeof(IMoneyCriticalCommand).IsAssignableFrom(typeof(DeletePlanCommand)).Should().BeTrue();
    }

    // ── Refusals ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PlanDeletionBlocker.Assignments)]
    [InlineData(PlanDeletionBlocker.Quotas)]
    [InlineData(PlanDeletionBlocker.Credits)]
    [InlineData(PlanDeletionBlocker.Payouts)]
    [InlineData(PlanDeletionBlocker.LedgerEntries)]
    [InlineData(PlanDeletionBlocker.ReconciliationClosures)]
    public async Task DraftWithAnyDependency_IsRefusedWithACode_AndNothingIsDeleted(PlanDeletionBlocker kind)
    {
        var plan = SeedDraft();
        SeedDependency(plan, kind);
        var command = new DeletePlanCommand(plan.Id);

        var act = () => Delete(command);

        var ex = (await act.Should().ThrowAsync<DomainCodedException>()).Which;
        ex.Code.Should().Be(PlanDeleteInvariant.HasDependencies);
        ex.Parameters["blockers"].Should().BeEquivalentTo(new[] { kind.ToString() });

        _db.ChangeTracker.Clear();
        _db.CompensationPlans.Any(p => p.Id == plan.Id).Should().BeTrue();
        _db.Set<Rule>().Count(r => r.PlanId == plan.Id).Should().Be(2);
        command.AuditMetadata.Should().BeNull("a refused delete describes nothing");
    }

    [Fact]
    public async Task DeactivatedAssignment_StillBlocks()
    {
        // The ticket said "active assignments". A deactivated one is still a dated fact pointing at the
        // plan (§B6); deleting the plan would orphan it.
        var plan = SeedDraft();
        SeedDependency(plan, PlanDeletionBlocker.Assignments);
        _db.PlanAssignments.Single(a => a.PlanId == plan.Id).Deactivate("system", Now, Guid.NewGuid());
        _db.SaveChanges();

        var act = () => Delete(new DeletePlanCommand(plan.Id));

        (await act.Should().ThrowAsync<DomainCodedException>()).Which.Code
            .Should().Be(PlanDeleteInvariant.HasDependencies);
    }

    [Fact]
    public async Task DraftWhoseRulesWereRemoved_ButThatHasCredits_IsRefused()
    {
        // The hole the old rule left open: it only asked "does the Draft have active rules?". A Draft
        // that generated credits (assigned through the API) and then had its rule removed passed it.
        var plan = SeedDraft(rules: 1);
        SeedDependency(plan, PlanDeletionBlocker.Credits);
        plan.RemoveRule(plan.Rules.First().Id);
        _db.SaveChanges();

        var act = () => Delete(new DeletePlanCommand(plan.Id));

        (await act.Should().ThrowAsync<DomainCodedException>()).Which.Code
            .Should().Be(PlanDeleteInvariant.HasDependencies);
    }

    [Fact]
    public async Task ActivePlan_IsRefusedAsNotDraft()
    {
        var plan = SeedDraft(activate: true);

        var act = () => Delete(new DeletePlanCommand(plan.Id));

        var ex = (await act.Should().ThrowAsync<DomainCodedException>()).Which;
        ex.Code.Should().Be(PlanDeleteInvariant.NotDraft);
        ex.Parameters["status"].Should().Be("Active");
        _db.CompensationPlans.Any(p => p.Id == plan.Id).Should().BeTrue();
    }

    [Fact]
    public async Task ArchivedPlan_IsRefusedAsNotDraft()
    {
        var plan = SeedDraft(activate: true);
        plan.Archive("system", Now, Guid.NewGuid());
        _db.SaveChanges();

        var act = () => Delete(new DeletePlanCommand(plan.Id));

        (await act.Should().ThrowAsync<DomainCodedException>()).Which.Code
            .Should().Be(PlanDeleteInvariant.NotDraft);
    }

    // ── The list says exactly what the handler will do ──────────────────────────────

    [Fact]
    public async Task ListIsDeletable_AgreesWithTheHandler_ForEveryCase()
    {
        // ★ §C3: the menu offers Delete from IsDeletable. If the flag and the handler disagreed, the screen
        // would offer a delete that fails, or hide one that works. So every case is asked of both.
        var clean = SeedDraft("A clean");
        var withQuota = SeedDraft("B quota");
        SeedDependency(withQuota, PlanDeletionBlocker.Quotas);
        var withCredit = SeedDraft("C credit");
        SeedDependency(withCredit, PlanDeletionBlocker.Credits);
        var active = SeedDraft("D active", activate: true);

        var list = await new ListPlansHandler(_db, _auth).Handle(
            new ListPlansQuery(new PaginationQuery { Page = 1, PageSize = 20 }), CancellationToken.None);
        var flags = list.Value!.Items.ToDictionary(p => p.Id, p => p.IsDeletable);

        flags[clean.Id].Should().BeTrue();
        flags[withQuota.Id].Should().BeFalse();
        flags[withCredit.Id].Should().BeFalse();
        flags[active.Id].Should().BeFalse();

        foreach (var plan in new[] { withQuota, withCredit, active, clean })
        {
            // One request per operation, as in production: the DbContext is scoped per request, so each
            // delete starts with nothing tracked from the list query or from the previous delete.
            _db.ChangeTracker.Clear();
            bool deleted;
            try { deleted = (await Delete(new DeletePlanCommand(plan.Id))).IsSuccess; }
            catch (DomainCodedException) { deleted = false; }

            deleted.Should().Be(flags[plan.Id], $"the list and the handler must agree on '{plan.Name}'");
        }
    }
}
