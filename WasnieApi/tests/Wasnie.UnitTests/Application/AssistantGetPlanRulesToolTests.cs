using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.Tools;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Plans;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The assistant's SECOND read-only tool: reading a plan's real configuration.
///
/// ★ THE GUARDS ARE NOT MOCKED, for the same reason they are not in the get_transaction tests. The tool
/// is wired to the REAL <c>ListPlansHandler</c> and <c>GetPlanByIdHandler</c>, over a REAL
/// <c>ApplicationDbContext</c> whose tenant query filter is live, behind a permission service that
/// answers from an actual role. Stubbing <c>ISender</c> would prove the tool calls something; the
/// property worth proving is that a user cannot read a plan they have no business reading, and only the
/// machinery that stops them can demonstrate that.
/// </summary>
public sealed class AssistantGetPlanRulesToolTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    private sealed class RoleAuthorization(params string[] permissions) : IAuthorizationService
    {
        private readonly HashSet<string> _granted = new(permissions, StringComparer.OrdinalIgnoreCase);

        public Task RequireAsync(string permission, CancellationToken cancellationToken = default) =>
            _granted.Contains(permission)
                ? Task.CompletedTask
                : throw new ForbiddenException(permission);

        // Same set, asked instead of enforced.
        public Task<bool> HasAsync(string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(_granted.Contains(permission));
    }

    /// <summary>Dispatches to the REAL query handlers, explicitly, so no handler can skip a guard.</summary>
    private sealed class HandlerSender(IApplicationDbContext db, IAuthorizationService auth) : ISender
    {
        public int PlanListQueries { get; private set; }

        public int PlanDetailQueries { get; private set; }

        /// <summary>Makes the list query come back as a FAILED Result — a fault, not an empty answer.</summary>
        public bool FailPlanListQuery { get; set; }

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            switch (request)
            {
                case ListPlansQuery q:
                    PlanListQueries++;

                    if (FailPlanListQuery)
                    {
                        return (TResponse)(object)Result<PagedResult<PlanSummaryDto>>.Failure(
                            "Unknown sort field.");
                    }

                    return (TResponse)(object)await new ListPlansHandler(db, auth).Handle(q, cancellationToken);

                case GetPlanByIdQuery q:
                    PlanDetailQueries++;
                    return (TResponse)(object)await new GetPlanByIdHandler(db, auth).Handle(q, cancellationToken);

                default:
                    throw new NotSupportedException($"Unexpected query {request.GetType().Name}.");
            }
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record Harness(
        ApplicationDbContext Db, GetPlanRulesTool Tool, HandlerSender Sender, Guid TenantId);

    private static Harness Build(string dbName, Guid tenantId, params string[] permissions)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        // ★ ONE database, addressed by name, so two harnesses with DIFFERENT tenants see the SAME rows
        // and only the query filter separates them. A per-test database would make the isolation test
        // pass for the wrong reason — there would be nothing to leak.
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(AssistantGetPlanRulesToolTests)}.{dbName}")
                .Options,
            tenantCtx, Substitute.For<IPublisher>());

        var sender = new HandlerSender(db, new RoleAuthorization(permissions));

        return new Harness(
            db, new GetPlanRulesTool(sender, NullLogger<GetPlanRulesTool>.Instance), sender, tenantId);
    }

    /// <param name="rules">Applied to the Draft plan before it is activated.</param>
    private static CompensationPlan Seed(
        ApplicationDbContext db,
        Guid tenantId,
        string name,
        Action<CompensationPlan> rules,
        bool activate = true,
        int? clawbackMaturationDays = null,
        decimal? clawbackCapPercent = null)
    {
        var plan = CompensationPlan.Create(
            tenantId: tenantId,
            name: name,
            description: "seeded",
            effectivePeriod: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            currency: Eur,
            createdBy: "seed",
            id: Guid.NewGuid(),
            now: Now,
            eventId: Guid.NewGuid());

        rules(plan);

        if (clawbackMaturationDays is not null || clawbackCapPercent is not null)
        {
            plan.SetClawbackPolicy(clawbackMaturationDays, clawbackCapPercent, "seed", Now);
        }

        if (activate)
        {
            plan.Activate("seed", Now, Guid.NewGuid());
        }

        db.CompensationPlans.Add(plan);
        db.SaveChanges();
        return plan;
    }

    private static void FlatRevenueRule(CompensationPlan plan, decimal rate = 0.05m) =>
        plan.AddRule("Base commission", 0, new Measurement { Type = MeasurementType.Revenue }, RateTable.Flat(rate));

    private static async Task<JsonElement> RunAsync(Harness h, string? planName)
    {
        var arguments = planName is null ? "{}" : $$"""{"planName":"{{planName}}"}""";
        var json = await h.Tool.RunAsync(arguments, CancellationToken.None);
        return JsonDocument.Parse(json).RootElement;
    }

    private static async Task<JsonElement> RunByIdAsync(Harness h, Guid planId)
    {
        var json = await h.Tool.RunAsync($$"""{"planId":"{{planId}}"}""", CancellationToken.None);
        return JsonDocument.Parse(json).RootElement;
    }

    // ── Recurring references: the id, not the name ────────────────────────────

    /// <summary>
    /// ★ THE SECOND TURN'S FIX. This tool's whole history is names arriving slightly wrong — an em-dash
    /// for a hyphen, a dropped quarter prefix — and a user being told a plan they were looking at does
    /// not exist. The payload now carries the plan's id so the next question copies an identifier
    /// instead of retyping a title.
    /// </summary>
    [Fact]
    public async Task The_payload_carries_the_plan_id_so_a_later_turn_can_reuse_it()
    {
        var tenant = Guid.NewGuid();
        var h = Build(nameof(The_payload_carries_the_plan_id_so_a_later_turn_can_reuse_it),
            tenant, Permission.PlansRead);
        var plan = Seed(h.Db, tenant, "Q3 2026 — Plan Comercial EMEA", p => FlatRevenueRule(p));

        var payload = await RunAsync(h, "Q3 2026 — Plan Comercial EMEA");

        payload.GetProperty("planId").GetGuid().Should().Be(plan.Id);
    }

    [Fact]
    public async Task An_id_resolves_the_plan_without_matching_a_name()
    {
        var tenant = Guid.NewGuid();
        var h = Build(nameof(An_id_resolves_the_plan_without_matching_a_name),
            tenant, Permission.PlansRead);
        var plan = Seed(h.Db, tenant, "Q3 2026 — Plan Comercial EMEA", p => FlatRevenueRule(p));

        var payload = await RunByIdAsync(h, plan.Id);

        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("planName").GetString().Should().Be("Q3 2026 — Plan Comercial EMEA");
        payload.GetProperty("matchedBy").GetString().Should().Be("ExactName");
        payload.GetProperty("rules").GetArrayLength().Should().BeGreaterThan(0);
    }

    /// <summary>
    /// ★ THE ID BUYS PRECISION, NEVER ACCESS. Another tenant's plan id takes the SAME refusal as an id
    /// that never existed — the tenant query filter inside GetPlanByIdQuery decides, not this tool.
    /// </summary>
    [Fact]
    public async Task An_id_from_another_tenant_is_refused_exactly_like_an_unknown_one()
    {
        const string db = nameof(An_id_from_another_tenant_is_refused_exactly_like_an_unknown_one);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var owner = Build(db, tenantA, Permission.PlansRead);
        var plan = Seed(owner.Db, tenantA, "Tenant A Plan", p => FlatRevenueRule(p));

        var outsider = Build(db, tenantB, Permission.PlansRead);

        var foreign = await RunByIdAsync(outsider, plan.Id);
        var imaginary = await RunByIdAsync(outsider, Guid.NewGuid());

        foreign.GetRawText().Should().Be(imaginary.GetRawText());
        foreign.GetProperty("found").GetBoolean().Should().BeFalse();
    }

    // ── Rule 1 — the lookup goes through the domain ───────────────────────────

    [Fact]
    public async Task The_tool_reads_the_plan_THROUGH_the_domain_queries()
    {
        var tenant = Guid.NewGuid();
        var h = Build(nameof(The_tool_reads_the_plan_THROUGH_the_domain_queries), tenant, Permission.PlansRead);
        Seed(h.Db, tenant, "EMEA Enterprise", p => FlatRevenueRule(p));

        var result = await RunAsync(h, "EMEA Enterprise");

        h.Sender.PlanListQueries.Should().Be(1, "the domain query IS the access path");
        h.Sender.PlanDetailQueries.Should().Be(1);
        result.GetProperty("found").GetBoolean().Should().BeTrue();
        result.GetProperty("outcome").GetString().Should().Be("PlanRules");
        result.GetProperty("planName").GetString().Should().Be("EMEA Enterprise");
        result.GetProperty("currencyCode").GetString().Should().Be(Eur);
    }

    [Fact]
    public async Task One_plan_never_answers_for_another_WITHOUT_SAYING_SO()
    {
        // ★ THIS TEST CHANGED, DELIBERATELY, AND HERE IS THE REASONING. It used to demand a flat refusal
        // for "EMEA" when only "EMEA Overlay" exists, to stop one plan answering with another's rates.
        // The concern was right; the remedy was too blunt. A user who is told "no such plan" while
        // looking at the only plan it could mean has been misinformed — which is exactly the incident
        // this WI was opened for, in a different costume.
        //
        // What actually protects them is not refusing, it is NOT LYING: one candidate is resolved, and
        // the payload states that the name was not exact and names the plan being described. The
        // forbidden thing — silently substituting a different plan — is what `matchedBy` makes
        // impossible, and rule 12b makes the model say it out loud.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(One_plan_never_answers_for_another_WITHOUT_SAYING_SO), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "EMEA Overlay", p => FlatRevenueRule(p));

        var result = await RunAsync(h, "EMEA");

        result.GetProperty("found").GetBoolean().Should().BeTrue();
        result.GetProperty("planName").GetString().Should().Be("EMEA Overlay");
        result.GetProperty("matchedBy").GetString().Should().Be("PartialNameSingleCandidate",
            "the user asked for a name that does not exist and MUST be told which plan this is");
    }

    // ── ★ THE NAME CAME OUT OF A LANGUAGE MODEL ───────────────────────────────

    [Theory]
    [InlineData(" q3 2026 — plan comercial ", "★ the reported incident: em-dash, padding and case")]
    [InlineData("Q3 2026 – Plan Comercial", "en dash")]
    [InlineData("Q3  2026  -  Plan  Comercial", "doubled spaces")]
    [InlineData("q3 2026 - plan comercial", "lower case")]
    public async Task A_plan_is_found_even_when_the_model_RETYPED_its_name(string asTypedByModel, string why)
    {
        // ★ THE INCIDENT, END TO END. The assistant explained this plan and two messages later said it
        // did not exist, because it rewrote the hyphen as an em-dash when composing its second tool
        // call. Both halves of the fix are exercised here: the SQL narrowing key has to FETCH the row
        // (the raw em-dash name matched nothing at all), and the comparison has to ACCEPT it.
        var tenant = Guid.NewGuid();
        var h = Build($"{nameof(A_plan_is_found_even_when_the_model_RETYPED_its_name)}.{why}", tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Q3 2026 - Plan Comercial", p => FlatRevenueRule(p));

        var result = await RunAsync(h, asTypedByModel);

        result.GetProperty("found").GetBoolean().Should().BeTrue(why);
        // The name reported back is the STORED one, not the model's retyping — the user reads the title
        // as it appears on their own screen.
        result.GetProperty("planName").GetString().Should().Be("Q3 2026 - Plan Comercial");
    }

    [Fact]
    public async Task A_plan_STORED_with_an_em_dash_is_found_by_a_plain_hyphen_too()
    {
        // Normalising only the request would fix the model and break the tenant who pasted a title out
        // of a document. Both sides are folded, so it works in both directions.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(A_plan_STORED_with_an_em_dash_is_found_by_a_plain_hyphen_too), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Q3 2026 — Plan Comercial", p => FlatRevenueRule(p));

        var result = await RunAsync(h, "Q3 2026 - Plan Comercial");

        result.GetProperty("found").GetBoolean().Should().BeTrue();
    }

    // ── ★ THE MODEL DROPS WORDS OUT OF THE NAME ───────────────────────────────

    [Theory]
    [InlineData("Plan Comercial EMEA (Test Integral)", "★ the reported incident: the quarter prefix dropped")]
    [InlineData("Q3 2026 — Plan Comercial EMEA", "the parenthetical dropped")]
    [InlineData("Plan Comercial EMEA", "both ends dropped")]
    public async Task A_plan_is_found_when_the_model_drops_WORDS_from_the_name(
        string asSentByModel, string why)
    {
        // ★ MEASURED, NOT GUESSED. Asked "Me puedes explicar el plan Q3 2026 — Plan Comercial EMEA (Test
        // Integral)", this model sent the full name five times out of eight and dropped the quarter
        // prefix the other three. Exact matching refused those three and the user — looking at the plan
        // on screen — was told it did not exist. Stochastically, which is the worst way to be told.
        var tenant = Guid.NewGuid();
        var h = Build($"{nameof(A_plan_is_found_when_the_model_drops_WORDS_from_the_name)}.{why}", tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Q3 2026 — Plan Comercial EMEA (Test Integral)", p => FlatRevenueRule(p));

        var result = await RunAsync(h, asSentByModel);

        result.GetProperty("found").GetBoolean().Should().BeTrue(why);
        result.GetProperty("planName").GetString().Should().Be("Q3 2026 — Plan Comercial EMEA (Test Integral)");

        // ★ AND IT SAYS THE NAME WAS NOT EXACT. This is what makes resolving a partial name safe rather
        // than a guess wearing a fact's clothes: the user is told which plan they are being shown.
        result.GetProperty("matchedBy").GetString().Should().Be("PartialNameSingleCandidate");
    }

    [Fact]
    public async Task An_exactly_named_plan_says_so_rather_than_claiming_a_near_miss()
    {
        var tenant = Guid.NewGuid();
        var h = Build(nameof(An_exactly_named_plan_says_so_rather_than_claiming_a_near_miss), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Q3 2026 — Plan Comercial EMEA (Test Integral)", p => FlatRevenueRule(p));

        var result = await RunAsync(h, "Q3 2026 - Plan Comercial EMEA (Test Integral)");

        result.GetProperty("matchedBy").GetString().Should().Be("ExactName",
            "typographic folding is still an EXACT match — only a missing WORD is a partial one");
    }

    [Fact]
    public async Task When_a_partial_name_could_mean_TWO_plans_the_tool_asks_instead_of_choosing()
    {
        // ★ THE LINE. One candidate means there is no wrong plan to answer about. Two means there is,
        // and picking one would put another plan's rates in front of somebody who believes them.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(When_a_partial_name_could_mean_TWO_plans_the_tool_asks_instead_of_choosing),
            tenant, Permission.PlansRead);
        Seed(h.Db, tenant, "Q3 2026 — Plan Comercial EMEA", p => FlatRevenueRule(p, 0.11m));
        Seed(h.Db, tenant, "Plan Comercial EMEA (Test Integral)", p => FlatRevenueRule(p, 0.03m));

        var result = await RunAsync(h, "Plan Comercial EMEA");

        result.GetProperty("outcome").GetString().Should().Be("PlanNameRequired");
        result.GetProperty("found").GetBoolean().Should().BeFalse();
        result.ToString().Should().NotContain("0.11").And.NotContain("0.03",
            "neither plan's rates may be shown while it is unclear which one was meant");
        result.GetProperty("availablePlans").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString())
            .Should().BeEquivalentTo("Q3 2026 — Plan Comercial EMEA", "Plan Comercial EMEA (Test Integral)");
    }

    [Fact]
    public async Task Two_VERSIONS_of_one_plan_are_one_candidate_not_an_ambiguity()
    {
        // Asking "which of these two?" about two rows with the identical title is a question the user
        // cannot answer. Candidates are counted by NAME, so a cloned plan resolves to its live version.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(Two_VERSIONS_of_one_plan_are_one_candidate_not_an_ambiguity), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Q3 2026 — Plan Comercial EMEA (Test Integral)", p => FlatRevenueRule(p),
            activate: false);
        Seed(h.Db, tenant, "Q3 2026 — Plan Comercial EMEA (Test Integral)", p => FlatRevenueRule(p));

        var result = await RunAsync(h, "Plan Comercial EMEA (Test Integral)");

        result.GetProperty("found").GetBoolean().Should().BeTrue();
        result.GetProperty("planStatus").GetString().Should().Be("Active", "the live version wins");
        result.GetProperty("matchedBy").GetString().Should().Be("PartialNameSingleCandidate");
    }

    [Fact]
    public async Task A_partial_name_NEVER_reaches_into_another_tenant()
    {
        // ★ THE FIX LOOSENED THE MATCH AGAIN, SO THE ISOLATION TEST IS REPEATED AGAINST IT. What
        // protects the row is the tenant query filter, not how strictly the name is compared.
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        const string db = nameof(A_partial_name_NEVER_reaches_into_another_tenant);

        var ownerHarness = Build(db, owner, Permission.PlansRead);
        Seed(ownerHarness.Db, owner, "Q3 2026 — Plan Comercial EMEA (Test Integral)",
            p => FlatRevenueRule(p, 0.42m));

        var strangerHarness = Build(db, stranger, Permission.PlansRead);
        var refused = await RunAsync(strangerHarness, "Plan Comercial EMEA");
        var nonexistent = await RunAsync(strangerHarness, "No Such Plan At All");

        refused.GetProperty("found").GetBoolean().Should().BeFalse();
        refused.ToString().Should().NotContain("0.42").And.NotContain("Test Integral");
        refused.ToString().Should().Be(nonexistent.ToString(), "the refusal still says nothing");
    }

    [Fact]
    public async Task A_name_with_NO_candidate_still_gets_the_UNCHANGED_refusal()
    {
        // ★ RULE 3 RESTS ON THIS PATH, so it is the one path the fix does not touch: when nothing could
        // be meant, the answer is the same single sentence it has always been — never a list, never a
        // hint that the tenant has plans at all.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(A_name_with_NO_candidate_still_gets_the_UNCHANGED_refusal), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Q3 2026 — Plan Comercial EMEA (Test Integral)", p => FlatRevenueRule(p));

        var result = await RunAsync(h, "Programa de Incentivos LATAM");

        result.GetProperty("outcome").GetString().Should().Be("NotFoundOrNotVisible");
        result.GetProperty("message").GetString().Should().Be(GetPlanRulesTool.RefusalMessage);
        result.TryGetProperty("availablePlans", out _).Should().BeFalse(
            "a name that matches nothing must not become a tour of the tenant's plans");
    }

    [Fact]
    public async Task Forgiving_TYPOGRAPHY_does_not_make_the_lookup_forgiving_about_WORDS()
    {
        // ★ THE LINE THE FIX MUST NOT CROSS. Two plans sharing a prefix must not collapse into one:
        // explaining the wrong plan's rates is a confident answer about somebody else's money, which is
        // worse than the refusal this WI set out to remove.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(Forgiving_TYPOGRAPHY_does_not_make_the_lookup_forgiving_about_WORDS), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Q3_Enterprise", p => FlatRevenueRule(p, 0.11m));
        Seed(h.Db, tenant, "Q3_SMB", p => FlatRevenueRule(p, 0.03m));

        var ambiguous = await RunAsync(h, "Q3");
        var wrongWord = await RunAsync(h, "Q3 2026 — Plan Comercial");

        ambiguous.GetProperty("found").GetBoolean().Should().BeFalse("a fragment is not a name");
        ambiguous.ToString().Should().NotContain("0.11").And.NotContain("0.03");
        wrongWord.GetProperty("found").GetBoolean().Should().BeFalse();

        // ★ AND IT IS STILL A BUSINESS RESULT, NOT A FAULT. The refusal payload is the ordinary one, so
        // the runner reports Completed and the user gets an answer — not the retry card that a broken
        // lookup earns.
        ambiguous.GetProperty("outcome").GetString().Should().Be("NotFoundOrNotVisible");
        ambiguous.GetProperty("message").GetString().Should().Be(GetPlanRulesTool.RefusalMessage);
    }

    // ── Rule 2 — ★ ISOLATION ──────────────────────────────────────────────────

    [Fact]
    public async Task A_user_CANNOT_read_a_plan_belonging_to_another_tenant()
    {
        // ★ Both harnesses share one database. The plan is REALLY there; only the tenant query filter
        // stands between the asking user and it. Remove that filter and this test goes red — which is
        // the only way to know the filter is what is protecting the row.
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        const string db = nameof(A_user_CANNOT_read_a_plan_belonging_to_another_tenant);

        var ownerHarness = Build(db, owner, Permission.PlansRead);
        Seed(ownerHarness.Db, owner, "Confidential EMEA", p => FlatRevenueRule(p, 0.42m));

        var strangerHarness = Build(db, stranger, Permission.PlansRead);
        var refused = await RunAsync(strangerHarness, "Confidential EMEA");

        refused.GetProperty("found").GetBoolean().Should().BeFalse();
        refused.ToString().Should().NotContain("0.42", "no rate of another tenant's plan may leak");

        // ★ RULE 3 — and this is the assertion that enforces it. The refusal for a plan that EXISTS but
        // belongs to somebody else must be BYTE-IDENTICAL to the refusal for a plan that does not exist.
        // Any difference at all — a word, a field, a length — is the confirmation an attacker is
        // fishing for.
        var nonexistent = await RunAsync(strangerHarness, "No Such Plan At All");
        refused.ToString().Should().Be(nonexistent.ToString());
    }

    [Fact]
    public async Task Typographic_tolerance_does_NOT_reach_across_tenants()
    {
        // ★ THE FIX LOOSENED A COMPARISON, AND A LOOSER COMPARISON IS EXACTLY WHERE ISOLATION GOES
        // WRONG. What protects the row is the tenant query filter, not the strictness of the name
        // match — so a name that WOULD now match must still find nothing from outside the tenant.
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        const string db = nameof(Typographic_tolerance_does_NOT_reach_across_tenants);

        var ownerHarness = Build(db, owner, Permission.PlansRead);
        Seed(ownerHarness.Db, owner, "Q3 2026 - Plan Comercial", p => FlatRevenueRule(p, 0.42m));

        var strangerHarness = Build(db, stranger, Permission.PlansRead);
        var refused = await RunAsync(strangerHarness, " q3 2026 — plan comercial ");
        var nonexistent = await RunAsync(strangerHarness, "No Such Plan At All");

        refused.GetProperty("found").GetBoolean().Should().BeFalse();
        refused.ToString().Should().NotContain("0.42");
        refused.ToString().Should().Be(nonexistent.ToString(), "the refusal still says nothing");
    }

    [Fact]
    public async Task A_user_WITHOUT_the_plans_permission_gets_the_SAME_answer_as_for_a_missing_plan()
    {
        // ★ RULE 3, the other half: "you may not read plans" and "there is no such plan" are one
        // sentence. A user who is told the refusal is about PERMISSION has been told the plan exists.
        var tenant = Guid.NewGuid();
        const string db = nameof(A_user_WITHOUT_the_plans_permission_gets_the_SAME_answer_as_for_a_missing_plan);

        var admin = Build(db, tenant, Permission.PlansRead);
        Seed(admin.Db, tenant, "Restricted Plan", p => FlatRevenueRule(p));

        var unprivileged = Build(db, tenant /* no permissions at all */);
        var refused = await RunAsync(unprivileged, "Restricted Plan");
        var nonexistent = await RunAsync(admin, "No Such Plan At All");

        refused.ToString().Should().Be(nonexistent.ToString());
        refused.GetProperty("message").GetString().Should().Be(GetPlanRulesTool.RefusalMessage);
    }

    // ── ★ A TECHNICAL FAILURE IS LOUD, NEVER A REFUSAL ────────────────────────

    [Fact]
    public async Task A_BROKEN_query_throws_instead_of_reporting_that_the_plan_does_not_exist()
    {
        // ★ THE LESSON OF THE STOCHASTIC BUG. A fault that comes back as "no such plan" is the assistant
        // denying data the user can see on their own screen. It must reach the runner as an exception so
        // the turn fails and the user gets the retry card — "try again" is true, "you have no such plan"
        // is not.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(A_BROKEN_query_throws_instead_of_reporting_that_the_plan_does_not_exist), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "EMEA Enterprise", p => FlatRevenueRule(p));
        h.Sender.FailPlanListQuery = true;

        var run = async () => await RunAsync(h, "EMEA Enterprise");

        await run.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── The semantics, end to end through the tool ────────────────────────────

    [Fact]
    public async Task A_flat_revenue_rate_is_reported_as_a_FRACTION_of_the_base()
    {
        var tenant = Guid.NewGuid();
        var h = Build(nameof(A_flat_revenue_rate_is_reported_as_a_FRACTION_of_the_base), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Full Rate", p => p.AddRule(
            "Pays everything", 0,
            new Measurement { Type = MeasurementType.Revenue },
            RateTable.Flat(1.00m),
            cap: new Cap { Amount = Money.Of(200m, Eur), Scope = CapScope.PerTransaction }));

        var rule = (await RunAsync(h, "Full Rate")).GetProperty("rules")[0];

        rule.GetProperty("rateTable").GetProperty("semanticBehavior").GetString()
            .Should().Be(nameof(RateSemantic.FractionalMultiplierOfBase));
        // ★ THE RAW VALUE IS NOT TRANSFORMED. 1.00 is sent as 1.00; the prompt teaches the model that
        // this token means "a fraction", so it can say 100% without the backend ever converting — the
        // conversion is what the strict numeric rule forbids.
        rule.GetProperty("rateTable").GetProperty("rawValue").GetDecimal().Should().Be(1.00m);
        rule.GetProperty("measurementBase").GetString().Should().Be(nameof(MeasurementBase.TransactionAmount));

        var cap = rule.GetProperty("cap");
        cap.GetProperty("rawAmount").GetDecimal().Should().Be(200m);
        cap.GetProperty("enforcement").GetString().Should().Be("EnforcedPerTransaction");
    }

    [Fact]
    public async Task A_units_rate_is_reported_as_MONEY_PER_UNIT_not_as_a_fraction()
    {
        var tenant = Guid.NewGuid();
        var h = Build(nameof(A_units_rate_is_reported_as_MONEY_PER_UNIT_not_as_a_fraction), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Per Unit", p => p.AddRule(
            "Two euros a licence", 0,
            new Measurement { Type = MeasurementType.Units },
            RateTable.Flat(2.00m)));

        var rule = (await RunAsync(h, "Per Unit")).GetProperty("rules")[0];

        rule.GetProperty("rateTable").GetProperty("semanticBehavior").GetString()
            .Should().Be(nameof(RateSemantic.CurrencyAmountPerUnit));
        rule.GetProperty("measurementBase").GetString().Should().Be(nameof(MeasurementBase.TransactionQuantity));
    }

    [Fact]
    public async Task Tiered_and_attainment_tables_travel_with_their_brackets()
    {
        var tenant = Guid.NewGuid();
        var h = Build(nameof(Tiered_and_attainment_tables_travel_with_their_brackets), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Layered", p =>
        {
            p.AddRule("Progressive", 0,
                new Measurement { Type = MeasurementType.Revenue },
                RateTable.Tiered([
                    new RateTier { From = 0m, To = 5_000m, Rate = 0.02m },
                    new RateTier { From = 5_000m, To = null, Rate = 0.10m },
                ]));
            p.AddRule("Against quota", 1,
                new Measurement { Type = MeasurementType.Revenue },
                RateTable.AttainmentBased(
                    [new AttainmentTier { AttainmentFrom = 1.00m, AttainmentTo = null, Rate = 0.12m }],
                    splitAtQuota: true));
        });

        var rules = (await RunAsync(h, "Layered")).GetProperty("rules");

        var tiered = rules[0].GetProperty("rateTable");
        tiered.GetProperty("semanticBehavior").GetString()
            .Should().Be(nameof(RateSemantic.FractionalRatePerRevenueBracket));
        tiered.GetProperty("amountTiers").GetArrayLength().Should().Be(2);
        tiered.GetProperty("amountTiers")[0].GetProperty("rawRate").GetDecimal().Should().Be(0.02m);
        // The open-ended top bracket has no upper bound, and the payload says so by omitting it rather
        // than printing a zero the model would read as a ceiling.
        tiered.GetProperty("amountTiers")[1].TryGetProperty("toAmount", out _).Should().BeFalse();

        var attainment = rules[1].GetProperty("rateTable");
        attainment.GetProperty("semanticBehavior").GetString()
            .Should().Be(nameof(RateSemantic.FractionalRateSplitAtQuotaBoundary));
        attainment.GetProperty("attainmentTiers")[0].GetProperty("fromAttainmentFraction").GetDecimal()
            .Should().Be(1.00m);
    }

    // ── The facts the model could not have inferred ───────────────────────────

    [Fact]
    public async Task A_cap_the_engine_does_NOT_enforce_is_reported_as_not_enforced()
    {
        // ★ THE MOST DANGEROUS FIELD ON A PLAN. Only PerTransaction caps are honoured; PerPeriod and
        // Total are saved and skipped. Telling a user "you are capped at €500" when nothing enforces it
        // is worse than saying nothing at all.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(A_cap_the_engine_does_NOT_enforce_is_reported_as_not_enforced), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Period Cap", p =>
        {
            p.AddRule("Capped per period", 0,
                new Measurement { Type = MeasurementType.Revenue }, RateTable.Flat(0.08m),
                cap: new Cap { Amount = Money.Of(500m, Eur), Scope = CapScope.PerPeriod });
            p.AddRule("Capped in the wrong currency", 1,
                new Measurement { Type = MeasurementType.Revenue }, RateTable.Flat(0.08m),
                cap: new Cap { Amount = Money.Of(500m, "USD"), Scope = CapScope.PerTransaction });
        });

        var rules = (await RunAsync(h, "Period Cap")).GetProperty("rules");

        rules[0].GetProperty("cap").GetProperty("enforcement").GetString()
            .Should().Be("NotEnforcedScopeNotImplemented");
        rules[1].GetProperty("cap").GetProperty("enforcement").GetString()
            .Should().Be("NotEnforcedCurrencyMismatch");
    }

    [Fact]
    public async Task A_modifier_with_conditions_is_reported_as_applying_UNCONDITIONALLY()
    {
        // ★ ApplyModifier multiplies by the factor and never evaluates Modifier.Trigger. An
        // administrator who configured a conditional accelerator has one that always fires — describing
        // the INTENT would confirm a belief that is costing money on every transaction.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(A_modifier_with_conditions_is_reported_as_applying_UNCONDITIONALLY), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Accelerated", p => p.AddRule(
            "Accelerated base", 0,
            new Measurement { Type = MeasurementType.Revenue }, RateTable.Flat(0.08m),
            modifier: new Modifier
            {
                Name = "Enterprise accelerator",
                Type = ModifierType.Accelerator,
                Factor = 1.5m,
                Trigger = Trigger.When(LogicalOperator.And, [
                    new Condition
                    {
                        Field = "category",
                        Operator = ConditionOperator.Equal,
                        Value = new ConditionValue { Type = ConditionValueType.String, Raw = "Enterprise" },
                    }]),
            }));

        var modifier = (await RunAsync(h, "Accelerated")).GetProperty("rules")[0].GetProperty("modifiers")[0];

        modifier.GetProperty("factor").GetDecimal().Should().Be(1.5m);
        modifier.GetProperty("semanticBehavior").GetString().Should().Be("MultipliesCommissionByFactor");
        modifier.GetProperty("conditionHandling").GetString()
            .Should().Be("ConditionsIgnoredModifierAlwaysApplies");
    }

    [Fact]
    public async Task A_condition_on_a_field_the_engine_does_not_know_is_reported_as_never_matching()
    {
        // ★ USUALLY THE ANSWER TO "why was I not paid". The engine resolves a condition's field through
        // TriggerFieldCatalog and treats an unknown name as "does not match", for ever.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(A_condition_on_a_field_the_engine_does_not_know_is_reported_as_never_matching),
            tenant, Permission.PlansRead);
        Seed(h.Db, tenant, "Typo Plan", p =>
        {
            p.AddRule("Unconditional one", 0,
                new Measurement { Type = MeasurementType.Revenue }, RateTable.Flat(0.05m));
            p.AddRule("Filtered one", 1,
                new Measurement { Type = MeasurementType.Revenue }, RateTable.Flat(0.05m),
                trigger: Trigger.When(LogicalOperator.And, [
                    new Condition
                    {
                        Field = "segment",           // not a field the engine reads
                        Operator = ConditionOperator.Equal,
                        Value = new ConditionValue { Type = ConditionValueType.String, Raw = "Enterprise" },
                    },
                    new Condition
                    {
                        Field = "category",         // one that is
                        Operator = ConditionOperator.In,
                        Value = new ConditionValue
                        {
                            Type = ConditionValueType.String,
                            Set = ["Enterprise", "Mid-Market"],
                        },
                    }]));
        });

        var rules = (await RunAsync(h, "Typo Plan")).GetProperty("rules");

        // ★ AN ABSOLUTE TOKEN, not the phrase "all transactions" — the model translates it into the
        // user's language, and a backend that shipped English would ship English to the ES and PL users.
        rules[0].GetProperty("triggerCondition").GetString().Should().Be("Unconditional");

        var conditions = rules[1].GetProperty("triggerCondition").GetProperty("conditions");
        conditions[0].GetProperty("fieldStatus").GetString().Should().Be("UnknownFieldRuleNeverMatches");
        conditions[1].GetProperty("fieldStatus").GetString().Should().Be("Recognised");
        conditions[1].GetProperty("values").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task The_calculation_ORDER_is_data_not_something_the_model_reconstructs()
    {
        // The assistant applied an accelerator and forgot the cap when it had to infer this order.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(The_calculation_ORDER_is_data_not_something_the_model_reconstructs), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Ordered", p => FlatRevenueRule(p), clawbackMaturationDays: 90,
            clawbackCapPercent: 50m);

        var result = await RunAsync(h, "Ordered");

        result.GetProperty("calculationOrder").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("RateTable", "Modifier", "Cap", "Floor");
        result.GetProperty("clawback").GetProperty("maturationDays").GetInt32().Should().Be(90);
    }

    // ── Naming a plan, and not naming one ─────────────────────────────────────

    [Fact]
    public async Task With_no_plan_named_and_several_visible_the_tool_asks_WHICH_instead_of_refusing()
    {
        // ★ NOT A REFUSAL. "Which plan?" reported as "not found" would tell a user with three plans
        // that they have none — a false statement about their own data, which is the failure this whole
        // piece exists to remove.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(With_no_plan_named_and_several_visible_the_tool_asks_WHICH_instead_of_refusing),
            tenant, Permission.PlansRead);
        Seed(h.Db, tenant, "Alpha", p => FlatRevenueRule(p));
        Seed(h.Db, tenant, "Beta", p => FlatRevenueRule(p));

        var result = await RunAsync(h, planName: null);

        result.GetProperty("outcome").GetString().Should().Be("PlanNameRequired");
        result.GetProperty("found").GetBoolean().Should().BeFalse();
        result.GetProperty("availablePlans").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString())
            .Should().BeEquivalentTo("Alpha", "Beta");
        h.Sender.PlanDetailQueries.Should().Be(0, "nothing was chosen, so nothing was opened");
    }

    [Fact]
    public async Task With_no_plan_named_and_exactly_one_visible_the_tool_answers_about_it()
    {
        var tenant = Guid.NewGuid();
        var h = Build(nameof(With_no_plan_named_and_exactly_one_visible_the_tool_answers_about_it), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "The Only Plan", p => FlatRevenueRule(p));

        var result = await RunAsync(h, planName: null);

        result.GetProperty("outcome").GetString().Should().Be("PlanRules");
        result.GetProperty("planName").GetString().Should().Be("The Only Plan");
    }

    // ── ★ GDPR — the payload carries no person ────────────────────────────────

    [Fact]
    public async Task The_payload_contains_NO_payee_field_and_no_persons_name()
    {
        // ★ A plan's configuration is not personal data, and this keeps it that way. The tool must never
        // grow a "who is on this plan" field: that is a different question, with a different lawful
        // basis, and it would arrive one innocuous-looking property at a time.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(The_payload_contains_NO_payee_field_and_no_persons_name), tenant,
            Permission.PlansRead);
        Seed(h.Db, tenant, "Clean Plan", p => FlatRevenueRule(p));

        var json = await h.Tool.RunAsync("""{"planName":"Clean Plan"}""", CancellationToken.None);

        foreach (var forbidden in new[]
                 {
                     "payee", "employeeCode", "fullName", "email", "managerId", "assignment",
                 })
        {
            json.Should().NotContainEquivalentOf(forbidden,
                $"a plan's rules never need '{forbidden}', and PII must not arrive by accident");
        }
    }

    // ── ★ EVERY TOKEN IS TAUGHT TO THE MODEL ──────────────────────────────────

    [Fact]
    public void The_system_prompt_defines_EVERY_token_the_tool_can_emit()
    {
        // ★ THE TEST THAT KEEPS THE CONTRACT HONEST. A token the prompt does not define is a token the
        // model INFERS, and inference over rate semantics is the exact failure this piece removes. This
        // walks the enums by reflection — including the private ones inside the tool — so adding a new
        // token without teaching it fails here instead of in front of a user.
        var tokenTypes = new List<Type> { typeof(RateSemantic), typeof(MeasurementBase) };

        tokenTypes.AddRange(typeof(GetPlanRulesTool)
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
            .Where(t => t.IsEnum));

        tokenTypes.Should().HaveCountGreaterThan(2, "the tool's own token enums must be discovered");

        var prompt = AssistantPrompt.PlanRuleTokenRules;

        foreach (var name in tokenTypes.SelectMany(Enum.GetNames).Distinct())
        {
            prompt.Should().Contain(name,
                $"the model must be told what '{name}' means — an undefined token is an invitation to guess");
        }
    }

    [Fact]
    public void The_token_rules_are_part_of_the_live_data_rules_the_model_actually_receives()
    {
        // Defining the tokens in a constant nobody sends would be worse than not defining them: the test
        // above would pass while the model still guessed.
        AssistantPrompt.DataRules.Should().Contain(AssistantPrompt.PlanRuleTokenRules);
    }
}
