using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.Handlers.Plans;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// THE EMERGENCY BRAKE (KAN-29). A rule of a LIVE plan can be stopped without cloning the plan.
///
/// ★ WHAT THESE TESTS ARE ACTUALLY GUARDING. The mechanism is one line in the engine — it filters
/// rules on <c>IsActive</c> (<c>CreditAllocationService.cs:332</c>) — so the risk was never that
/// stopping fails to work. It is that the marker gets confused with the OTHER meaning of a false
/// <c>IsActive</c> ("removed from a draft"), and a stopped rule then vanishes from the clone, from
/// the plan detail, or becomes uneditable. Most of what follows tests that distinction, not the stop.
/// </summary>
public sealed class StopRuleHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _auth;
    private readonly ICurrentUserService _currentUser;
    private readonly IClock _clock;

    public StopRuleHandlerTests()
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

        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.UserId.Returns("comp-manager-1");

        _clock = Substitute.For<IClock>();
        _clock.UtcNowOffset.Returns(Now);
    }

    public void Dispose() => _db.Dispose();

    private static Measurement Revenue() => new()
    {
        Type = MeasurementType.Revenue,
        SourceField = "amount",
        Aggregation = MeasurementAggregation.Sum,
    };

    private Plan SeedActivePlan(Guid planId, params string[] ruleNames)
    {
        var plan = Plan.Create(TenantId, "Test Plan", "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "EUR", "system", planId, Now, Guid.NewGuid());

        var order = 1;
        foreach (var name in ruleNames.Length == 0 ? ["Base"] : ruleNames)
        {
            plan.AddRule(name, order++, Revenue(), RateTable.Flat(0.05m));
        }

        plan.Activate("system", Now, Guid.NewGuid());
        _db.CompensationPlans.Add(plan);
        _db.SaveChanges();
        return plan;
    }

    private StopRuleHandler Handler() => new(_db, _currentUser, _clock, _auth);

    // ── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Stop_RecordsTheMarker_AndClearsIsActive()
    {
        var planId = Guid.NewGuid();
        var plan = SeedActivePlan(planId);
        var ruleId = plan.Rules.Single().Id;

        var result = await Handler().Handle(
            new StopRuleCommand(planId, ruleId, "Rate was 5% instead of 0.5%"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var rule = (await _db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId))
            .Rules.Single(r => r.Id == ruleId);

        rule.StoppedAt.Should().Be(Now);
        rule.StoppedBy.Should().Be("comp-manager-1");
        rule.StopReason.Should().Be("Rate was 5% instead of 0.5%");

        // ★ THE HALF THE ENGINE ACTUALLY READS. Without this the screen says "stopped" over a rule
        // that keeps paying, because CreditAllocationService filters on IsActive and nothing else.
        rule.IsActive.Should().BeFalse();
        rule.IsStopped.Should().BeTrue();
    }

    [Fact]
    public async Task Stop_ReturnsTheMarkerToTheCaller_SoTheScreenCanShowIt()
    {
        var planId = Guid.NewGuid();
        var plan = SeedActivePlan(planId);

        var result = await Handler().Handle(
            new StopRuleCommand(planId, plan.Rules.Single().Id, "  paying twice  "), CancellationToken.None);

        result.Value!.StoppedAt.Should().Be(Now);
        result.Value.StoppedBy.Should().Be("comp-manager-1");
        result.Value.StopReason.Should().Be("paying twice", "the reason is trimmed before it is stored");
        result.Value.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Stop_RequiresItsOwnPermission_NotPlansUpdate()
    {
        var planId = Guid.NewGuid();
        var plan = SeedActivePlan(planId);

        await Handler().Handle(
            new StopRuleCommand(planId, plan.Rules.Single().Id, "wrong rate"), CancellationToken.None);

        await _auth.Received(1).RequireAsync(Permission.PlansStopRule, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Only TenantAdmin and CompManager may pull the brake. Asserted against the role map itself
    /// rather than the handler: the handler asks for one permission, and WHO holds it is the decision.
    /// </summary>
    [Fact]
    public void StopRulePermission_IsHeldByTenantAdminAndCompManagerOnly()
    {
        Wasnie.Application.Authorization.RolePermissions
            .HasPermission("TenantAdmin", Permission.PlansStopRule).Should().BeTrue();
        Wasnie.Application.Authorization.RolePermissions
            .HasPermission("CompManager", Permission.PlansStopRule).Should().BeTrue();
        Wasnie.Application.Authorization.RolePermissions
            .HasPermission("Manager", Permission.PlansStopRule).Should().BeFalse();
        Wasnie.Application.Authorization.RolePermissions
            .HasPermission("Rep", Permission.PlansStopRule).Should().BeFalse();
    }

    // ── Edge case 1: the last rule ──────────────────────────────────────────

    /// <summary>
    /// 1,225 credits come from plans with exactly one rule, so refusing to stop the last one would
    /// leave the brake unusable for most of the plans that need it. The plan stays Active and does
    /// NOT change state on its own.
    /// </summary>
    [Fact]
    public async Task Stop_TheLastActiveRule_Succeeds_AndThePlanStaysActive()
    {
        var planId = Guid.NewGuid();
        var plan = SeedActivePlan(planId);

        var result = await Handler().Handle(
            new StopRuleCommand(planId, plan.Rules.Single().Id, "paying wrong"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var reloaded = await _db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
        reloaded.Status.Should().Be(PlanStatus.Active);
        reloaded.Rules.Any(r => r.IsActive).Should().BeFalse();
    }

    /// <summary>
    /// "A plan with no live rules" is DERIVED from the rules, never stored — a flag would drift the
    /// first time a rule was added back. The detail payload carries the rules the UI derives it from.
    /// </summary>
    [Fact]
    public async Task PlanWithEveryRuleStopped_IsVisibleAsSuch_FromTheRulesThemselves()
    {
        var planId = Guid.NewGuid();
        var plan = SeedActivePlan(planId, "Base", "Bonus");
        foreach (var id in plan.Rules.Select(r => r.Id).ToList())
        {
            await Handler().Handle(new StopRuleCommand(planId, id, "wrong"), CancellationToken.None);
        }

        var dto = (await new GetPlanByIdHandler(_db, _auth)
            .Handle(new GetPlanByIdQuery(planId), CancellationToken.None)).Value!;

        dto.Status.Should().Be(nameof(PlanStatus.Active));
        dto.Rules.Should().HaveCount(2, "a stopped rule is not a deleted one — it stays on screen");
        dto.Rules.Should().OnlyContain(r => r.StoppedAt != null && !r.IsActive);
    }

    // ── Edge case 2: irreversibility ────────────────────────────────────────

    [Fact]
    public async Task Stop_ARuleAlreadyStopped_IsRefusedWithItsOwnCode()
    {
        var planId = Guid.NewGuid();
        var plan = SeedActivePlan(planId);
        var ruleId = plan.Rules.Single().Id;

        await Handler().Handle(new StopRuleCommand(planId, ruleId, "first"), CancellationToken.None);

        var act = () => Handler().Handle(new StopRuleCommand(planId, ruleId, "second"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainCodedException>())
            .Which.Code.Should().Be(RuleStopInvariant.AlreadyStopped);
    }

    [Fact]
    public async Task Stop_DoesNotOverwriteTheOriginalReasonOrActor()
    {
        var planId = Guid.NewGuid();
        var plan = SeedActivePlan(planId);
        var ruleId = plan.Rules.Single().Id;

        await Handler().Handle(new StopRuleCommand(planId, ruleId, "the real reason"), CancellationToken.None);

        _currentUser.UserId.Returns("somebody-else");
        try
        {
            await Handler().Handle(new StopRuleCommand(planId, ruleId, "a later story"), CancellationToken.None);
        }
        catch (DomainCodedException) { /* expected — asserted above */ }

        var rule = (await _db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId))
            .Rules.Single(r => r.Id == ruleId);

        rule.StopReason.Should().Be("the real reason");
        rule.StoppedBy.Should().Be("comp-manager-1");
    }

    /// <summary>
    /// ★ THE INVARIANT THIS WHOLE FEATURE RESTS ON, asserted against the TYPE rather than a code path:
    /// no method, public or internal, sets StoppedAt back to null. The precedent being avoided is
    /// <c>Payee.Activate()</c>, which clears DeactivatedAt and erases the history. A reviewer adding a
    /// "resume" convenience method will fail here rather than in production.
    /// </summary>
    [Fact]
    public void Rule_ExposesNoWayToClearTheStopMarker()
    {
        var members = typeof(Rule)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
            .Select(m => m.Name)
            .ToList();

        members.Should().NotContain(n =>
            n.Contains("Resume", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Unstop", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Reactivate", StringComparison.OrdinalIgnoreCase) ||
            n.Equals("Activate", StringComparison.OrdinalIgnoreCase));

        // The setters stay private: nothing outside the entity can assign the marker at all.
        typeof(Rule).GetProperty(nameof(Rule.StoppedAt))!.SetMethod!.IsPrivate.Should().BeTrue();
        typeof(Rule).GetProperty(nameof(Rule.StopReason))!.SetMethod!.IsPrivate.Should().BeTrue();
    }

    // ── Edge case 3: cloning ────────────────────────────────────────────────

    /// <summary>
    /// A clone that dropped stopped rules would hand the next version a clean slate that quietly
    /// omits the rule someone braked — the silence this feature exists to end.
    /// </summary>
    [Fact]
    public async Task Clone_CarriesTheStoppedRule_StillStopped_WithItsDateAndReason()
    {
        var planId = Guid.NewGuid();
        var plan = SeedActivePlan(planId, "Base", "Bonus");
        var stoppedId = plan.Rules.First(r => r.Name == "Base").Id;

        await Handler().Handle(new StopRuleCommand(planId, stoppedId, "rate typo"), CancellationToken.None);

        var reloaded = await _db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
        var clone = reloaded.CloneAsNewVersion("system", Now, Guid.NewGuid);

        clone.Rules.Should().HaveCount(2);

        var clonedStopped = clone.Rules.Single(r => r.Name == "Base");
        clonedStopped.Id.Should().NotBe(stoppedId, "a cloned rule is a new rule");
        clonedStopped.IsStopped.Should().BeTrue("a clone is not a review — nothing here decides it was fixed");
        clonedStopped.StoppedAt.Should().Be(Now);
        clonedStopped.StopReason.Should().Be("rate typo");

        clone.Rules.Single(r => r.Name == "Bonus").IsActive.Should().BeTrue();
    }

    /// <summary>
    /// A rule REMOVED from a draft still must not travel. This is the test that would fail if the
    /// clone filter were loosened to "everything" instead of "active or stopped".
    /// </summary>
    [Fact]
    public void Clone_StillDropsRulesThatWereMerelyRemovedFromADraft()
    {
        var plan = Plan.Create(TenantId, "P", "d",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "EUR", "system", Guid.NewGuid(), Now, Guid.NewGuid());

        plan.AddRule("Keep", 1, Revenue(), RateTable.Flat(0.05m));
        var doomed = plan.AddRule("Removed", 2, Revenue(), RateTable.Flat(0.05m));
        plan.RemoveRule(doomed.Id);
        plan.Activate("system", Now, Guid.NewGuid());

        var clone = plan.CloneAsNewVersion("system", Now, Guid.NewGuid);

        clone.Rules.Should().ContainSingle().Which.Name.Should().Be("Keep");
    }

    /// <summary>
    /// ★ THE RECONCILIATION OF TWO ACCEPTANCE CRITERIA THAT LOOKED CONTRADICTORY: the marker never
    /// returns to null (EC2), AND editing the cloned rule in the Draft leaves an active rule (EC3).
    /// Both hold because the edit SUPERSEDES rather than revives — there is no unstop, there is a new
    /// rule. The stopped predecessor keeps its date and reason for whoever reads the Draft later.
    /// </summary>
    [Fact]
    public void EditingAStoppedRuleInADraft_ProducesANewActiveRule_AndLeavesTheStoppedOneStopped()
    {
        var plan = Plan.Create(TenantId, "P", "d",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "EUR", "system", Guid.NewGuid(), Now, Guid.NewGuid());
        plan.AddRule("Base", 1, Revenue(), RateTable.Flat(0.05m));
        plan.Activate("system", Now, Guid.NewGuid());
        plan.StopRule(plan.Rules.Single().Id, "someone", "rate typo", Now);

        var clone = plan.CloneAsNewVersion("system", Now, Guid.NewGuid);
        var stoppedInDraft = clone.Rules.Single();

        var corrected = clone.UpdateRule(stoppedInDraft.Id, "Base", 1, Revenue(), RateTable.Flat(0.005m));

        corrected.Id.Should().NotBe(stoppedInDraft.Id, "the correction is a NEW rule, not a revival");
        corrected.IsActive.Should().BeTrue();
        corrected.IsStopped.Should().BeFalse();

        stoppedInDraft.IsStopped.Should().BeTrue("nothing returns StoppedAt to null");
        stoppedInDraft.StopReason.Should().Be("rate typo");

        clone.Rules.Count(r => r.IsActive).Should().Be(1);
    }

    /// <summary>
    /// A Draft whose only rule arrived stopped cannot be activated — Activate demands a live rule.
    /// Not a defect: it forces the correction to be a deliberate act before the plan pays again.
    /// </summary>
    [Fact]
    public void ADraftWhoseRulesAllArrivedStopped_CannotBeActivatedUntilOneIsCorrected()
    {
        var plan = Plan.Create(TenantId, "P", "d",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "EUR", "system", Guid.NewGuid(), Now, Guid.NewGuid());
        plan.AddRule("Base", 1, Revenue(), RateTable.Flat(0.05m));
        plan.Activate("system", Now, Guid.NewGuid());
        plan.StopRule(plan.Rules.Single().Id, "someone", "rate typo", Now);

        var clone = plan.CloneAsNewVersion("system", Now, Guid.NewGuid);

        var act = () => clone.Activate("system", Now, Guid.NewGuid());
        act.Should().Throw<DomainException>();

        clone.UpdateRule(clone.Rules.Single().Id, "Base", 1, Revenue(), RateTable.Flat(0.005m));
        clone.Activate("system", Now, Guid.NewGuid());
        clone.Status.Should().Be(PlanStatus.Active);
    }

    // ── Fail-safe: validation ───────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Stop_WithoutAReason_IsRefusedWithItsOwnCode(string? reason)
    {
        var planId = Guid.NewGuid();
        var plan = SeedActivePlan(planId);

        var act = () => Handler().Handle(
            new StopRuleCommand(planId, plan.Rules.Single().Id, reason), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainCodedException>())
            .Which.Code.Should().Be(RuleStopInvariant.ReasonRequired);

        var rule = (await _db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId))
            .Rules.Single();
        rule.IsActive.Should().BeTrue("a refused stop must not half-apply");
    }

    [Fact]
    public async Task Stop_WithAnOverlongReason_IsRefused_AndSaysTheLimit()
    {
        var planId = Guid.NewGuid();
        var plan = SeedActivePlan(planId);

        var act = () => Handler().Handle(
            new StopRuleCommand(planId, plan.Rules.Single().Id, new string('x', 501)), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<DomainCodedException>()).Which;
        ex.Code.Should().Be(RuleStopInvariant.ReasonTooLong);
        ex.Parameters["maxLength"].Should().Be(Rule.StopReasonMaxLength);
    }

    [Fact]
    public async Task Stop_OnADraftPlan_IsRefused_BecauseThereIsNothingLiveToStop()
    {
        var plan = Plan.Create(TenantId, "P", "d",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "EUR", "system", Guid.NewGuid(), Now, Guid.NewGuid());
        plan.AddRule("Base", 1, Revenue(), RateTable.Flat(0.05m));
        _db.CompensationPlans.Add(plan);
        await _db.SaveChangesAsync();

        var act = () => Handler().Handle(
            new StopRuleCommand(plan.Id, plan.Rules.Single().Id, "wrong"), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<DomainCodedException>()).Which;
        ex.Code.Should().Be(RuleStopInvariant.PlanNotActive);
        ex.Parameters["status"].Should().Be(nameof(PlanStatus.Draft));
    }

    [Fact]
    public async Task Stop_AnUnknownRule_IsRefusedWithItsOwnCode()
    {
        var planId = Guid.NewGuid();
        SeedActivePlan(planId);

        var act = () => Handler().Handle(
            new StopRuleCommand(planId, Guid.NewGuid(), "wrong"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainCodedException>())
            .Which.Code.Should().Be(RuleStopInvariant.RuleNotFound);
    }

    /// <summary>
    /// The refusals must reach the browser as CODES. Result carries a single string, so a handler
    /// that caught DomainCodedException would deliver an English sentence with the code stripped —
    /// on the one dialog someone opens because money is going out wrong.
    /// </summary>
    [Fact]
    public async Task RefusalsAreRethrownAsCodes_NotFlattenedIntoAResultString()
    {
        var planId = Guid.NewGuid();
        var plan = SeedActivePlan(planId);

        var act = () => Handler().Handle(
            new StopRuleCommand(planId, plan.Rules.Single().Id, null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainCodedException>();
    }

    // ── Fail-safe: money ────────────────────────────────────────────────────

    /// <summary>
    /// Stopping is about the NEXT transaction, never the last one. Undoing what was already paid is a
    /// clawback — a different act, with a different audit trail and a different authorisation.
    /// </summary>
    [Fact]
    public async Task Stop_TouchesNoExistingCredit()
    {
        var planId = Guid.NewGuid();
        var plan = SeedActivePlan(planId);
        var ruleId = plan.Rules.Single().Id;

        var creditsBefore = await _db.Credits.CountAsync();

        await Handler().Handle(new StopRuleCommand(planId, ruleId, "wrong rate"), CancellationToken.None);

        (await _db.Credits.CountAsync()).Should().Be(creditsBefore);
    }

    // ── The audit row ───────────────────────────────────────────────────────

    /// <summary>
    /// The reason is recorded in BOTH places on purpose: on the rule, because that is the surface a
    /// reader actually opens, and in the audit metadata, because that answers "who braked this" for a
    /// rule a later clone-and-correct has already superseded.
    /// </summary>
    [Fact]
    public void StopCommand_CarriesTheReasonAndTheRuleIntoTheAuditRow()
    {
        var command = new StopRuleCommand(Guid.NewGuid(), Guid.NewGuid(), "  rate typo  ");

        command.AuditAction.Should().Be(Wasnie.Domain.Audit.AuditActions.PlanRuleStopped);
        command.AuditMetadata!["reason"].Should().Be("rate typo");
        command.AuditMetadata["ruleId"].Should().Be(command.RuleId.ToString());
    }
}
