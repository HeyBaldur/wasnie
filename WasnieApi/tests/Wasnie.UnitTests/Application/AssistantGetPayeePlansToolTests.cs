using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Assistant.Tools;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Assignments;
using Wasnie.Application.Compensation.Handlers.Payees;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Application.Compensation.Queries.Payees;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The assistant's FOURTH tool: which plans a person is assigned to.
///
/// ★ WHY IT EXISTS. "What plans does Ana have?" had no tool, so the dispatcher sent the question to the
/// only tool with "plan" in its name — <c>get_plan_rules</c>, with the PAYEE'S NAME as the plan name. No
/// plan is called "Ana García", the lookup refused, and the user was told Ana could not be found one turn
/// after being shown her balance. The reproduced bug.
///
/// ★ THE GUARDS ARE NOT MOCKED, exactly as in the other three tool suites. The tool is wired to the REAL
/// <c>ListPayeesHandler</c> and <c>ListAssignmentsByPayeeHandler</c> over a REAL DbContext with a live
/// tenant filter and a permission service that answers from an actual role. Stubbing ISender would prove
/// the tool calls something; what needs proving is that it cannot answer for a payee the caller has no
/// business reading.
/// </summary>
public sealed class AssistantGetPayeePlansToolTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    private sealed class RoleAuthorization(params string[] permissions) : IAuthorizationService
    {
        private readonly HashSet<string> _granted = new(permissions, StringComparer.OrdinalIgnoreCase);

        public Task RequireAsync(string permission, CancellationToken cancellationToken = default) =>
            _granted.Contains(permission)
                ? Task.CompletedTask
                : throw new ForbiddenException(permission);
    }

    /// <summary>Dispatches to the REAL handlers so no guard can be skipped.</summary>
    private sealed class HandlerSender(
        IApplicationDbContext db,
        IAuthorizationService auth,
        IPayeeAccessGuard guard,
        ITenantContext tenantContext,
        IClock clock) : ISender
    {
        /// <summary>Turns the payee lookup into a FAULT — not an empty answer. See the failure test.</summary>
        public bool FailPayeeListQuery { get; set; }

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            switch (request)
            {
                case ListPayeesQuery q:
                    if (FailPayeeListQuery)
                        return (TResponse)(object)Result<PagedResult<PayeeDto>>.Failure("Unknown sort field.");

                    return (TResponse)(object)await new ListPayeesHandler(db, tenantContext, auth)
                        .Handle(q, cancellationToken);

                case GetPayeeByIdQuery q:
                    return (TResponse)(object)await new GetPayeeByIdHandler(db, auth)
                        .Handle(q, cancellationToken);

                case ListAssignmentsByPayeeQuery q:
                    return (TResponse)(object)await new ListAssignmentsByPayeeHandler(db, auth, guard, clock)
                        .Handle(q, cancellationToken);

                default:
                    throw new NotSupportedException($"Unexpected query {request.GetType().Name}.");
            }
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record Harness(
        ApplicationDbContext Db, GetPayeePlansTool Tool, HandlerSender Sender, Guid TenantId);

    private static Harness Build(
        string dbName, Guid tenantId, PayeeVisibility? visibility = null, params string[] permissions)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        // ONE database addressed by name, so two harnesses with different tenants see the same rows and
        // only the query filter separates them — otherwise the isolation test passes for lack of data.
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(AssistantGetPayeePlansToolTests)}.{dbName}")
                .Options,
            tenantCtx, Substitute.For<IPublisher>());

        var guard = new FakePayeeAccessGuard(visibility ?? PayeeVisibility.Everything);
        var sender = new HandlerSender(
            db, new RoleAuthorization(permissions), guard, tenantCtx, new FakeClock(Now.UtcDateTime));

        return new Harness(
            db, new GetPayeePlansTool(sender, guard, NullLogger<GetPayeePlansTool>.Instance), sender,
            tenantId);
    }

    /// <summary>Exactly what the tool needs: find a payee, read their assignments. Nothing more.</summary>
    private static readonly string[] AssignmentPermissions =
        [Permission.PayeesRead, Permission.AssignmentsRead];

    private static Guid SeedPayee(Harness h, string fullName, string code)
    {
        var payee = Payee.Create(h.TenantId, fullName, code, $"{code}@test.com".ToLowerInvariant(),
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        h.Db.Payees.Add(payee);
        h.Db.SaveChanges();
        return payee.Id;
    }

    private static Guid SeedPlan(Harness h, string name)
    {
        var plan = CompensationPlan.Create(
            tenantId: h.TenantId,
            name: name,
            description: "seeded",
            effectivePeriod: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            currency: Eur,
            createdBy: "seed",
            id: Guid.NewGuid(),
            now: Now,
            eventId: Guid.NewGuid());

        h.Db.CompensationPlans.Add(plan);
        h.Db.SaveChanges();
        return plan.Id;
    }

    private static Guid SeedAssignment(
        Harness h, Guid payeeId, Guid planId, string payeeName, string code, bool deactivated = false)
    {
        var assignment = PlanAssignment.Create(
            h.TenantId, planId, payeeId,
            PayeeReference.Snapshot(payeeId, payeeName, code),
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "seed", Guid.NewGuid(), Now, Guid.NewGuid());

        if (deactivated)
            assignment.Deactivate("seed", Now, Guid.NewGuid());

        h.Db.PlanAssignments.Add(assignment);
        h.Db.SaveChanges();
        return assignment.Id;
    }

    private static async Task<JsonElement> RunAsync(Harness h, string argumentsJson) =>
        JsonDocument.Parse(await h.Tool.RunAsync(argumentsJson, default)).RootElement;

    private static Task<JsonElement> ByNameAsync(Harness h, string name) =>
        RunAsync(h, $$"""{"payeeName":"{{name}}"}""");

    private static Task<JsonElement> ByIdAsync(Harness h, Guid id) =>
        RunAsync(h, $$"""{"payeeId":"{{id}}"}""");

    // ══ THE QUESTION THAT HAD NO TOOL ═════════════════════════════════════════

    [Fact]
    public async Task A_payees_assignments_are_reported_with_plan_period_and_status()
    {
        var h = Build(nameof(A_payees_assignments_are_reported_with_plan_period_and_status),
            Guid.NewGuid(), null, AssignmentPermissions);
        var payeeId = SeedPayee(h, "Ana García", "EMP-ANA");
        SeedAssignment(h, payeeId, SeedPlan(h, "Q3 2026 — EMEA"), "Ana García", "EMP-ANA");

        var payload = await ByNameAsync(h, "Ana García");

        payload.GetProperty("outcome").GetString().Should().Be("PayeePlans");
        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("payeeName").GetString().Should().Be("Ana García");
        payload.GetProperty("assignmentCount").GetInt32().Should().Be(1);

        var assignment = payload.GetProperty("assignments").EnumerateArray().Single();
        assignment.GetProperty("planName").GetString().Should().Be("Q3 2026 — EMEA");
        assignment.GetProperty("assignmentStatus").GetString().Should().Be("Active");
        assignment.GetProperty("effectiveFrom").GetString().Should().Be("2026-01-01");
        assignment.GetProperty("effectiveTo").GetString().Should().Be("2026-12-31");
    }

    [Fact]
    public async Task THE_PAYEE_ID_IS_THE_FIRST_FIELD_so_the_next_turn_can_copy_it()
    {
        // ★ The conversation's memory. Whatever is asked about this person next — their balance, the
        // rules of one of these plans — starts from an id rather than a retyped name.
        var h = Build(nameof(THE_PAYEE_ID_IS_THE_FIRST_FIELD_so_the_next_turn_can_copy_it),
            Guid.NewGuid(), null, AssignmentPermissions);
        var payeeId = SeedPayee(h, "Ana García", "EMP-ANA");
        var planId = SeedPlan(h, "Q3 2026 — EMEA");
        SeedAssignment(h, payeeId, planId, "Ana García", "EMP-ANA");

        var raw = await h.Tool.RunAsync("""{"payeeName":"Ana García"}""", default);
        var payload = JsonDocument.Parse(raw).RootElement;

        payload.GetProperty("payeeId").GetGuid().Should().Be(payeeId);

        // ★ AND THE PLAN'S ID TRAVELS TOO — "how does that second plan pay?" is the next question, and
        // get_plan_rules takes a planId.
        payload.GetProperty("assignments").EnumerateArray().Single()
            .GetProperty("planId").GetGuid().Should().Be(planId);

        // The channel must actually be able to harvest it off this payload.
        var resolved = Wasnie.Application.Assistant.Common.ResolvedEntityContext.Extract(raw);
        resolved!.Payees.Should().ContainSingle().Which.Id.Should().Be(payeeId);
    }

    [Fact]
    public async Task THE_ID_PATH_RESOLVES_NOTHING_AND_ANSWERS_ABOUT_THE_SAME_PERSON()
    {
        // ★ THE BRANCH THE WORK ITEM EXISTS FOR: the follow-up turn names nobody, and the dispatcher
        // passes the id it was handed. Two payees whose names both contain "Ana" make the point — a name
        // lookup here would be ambiguous and refuse, and the id is unaffected.
        var h = Build(nameof(THE_ID_PATH_RESOLVES_NOTHING_AND_ANSWERS_ABOUT_THE_SAME_PERSON),
            Guid.NewGuid(), null, AssignmentPermissions);
        var ana = SeedPayee(h, "Ana García", "EMP-ANA");
        SeedPayee(h, "Ana María Ruiz", "EMP-ANA2");
        SeedAssignment(h, ana, SeedPlan(h, "Q3 2026 — EMEA"), "Ana García", "EMP-ANA");

        var payload = await ByIdAsync(h, ana);

        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("payeeId").GetGuid().Should().Be(ana);
        payload.GetProperty("matchedBy").GetString().Should().Be("ResolvedById");
        payload.GetProperty("payeeName").GetString().Should().Be("Ana García");
    }

    [Fact]
    public async Task An_employee_code_resolves_and_SAYS_the_name_is_one_the_user_did_not_type()
    {
        var h = Build(nameof(An_employee_code_resolves_and_SAYS_the_name_is_one_the_user_did_not_type),
            Guid.NewGuid(), null, AssignmentPermissions);
        var payeeId = SeedPayee(h, "Adrián Domínguez", "NB-2001");
        SeedAssignment(h, payeeId, SeedPlan(h, "Q3 2026 — EMEA"), "Adrián Domínguez", "NB-2001");

        var payload = await ByNameAsync(h, "NB-2001");

        payload.GetProperty("matchedBy").GetString().Should().Be("EmployeeCode");
        payload.GetProperty("payeeName").GetString().Should().Be("Adrián Domínguez",
            "★ rule 19a: the model must open with the full name instead of concluding it found nobody");
    }

    // ══ THE EMPTY RESULT THAT MUST NOT BECOME A CLAIM ═════════════════════════

    [Fact]
    public async Task A_PAYEE_WITH_NO_VISIBLE_ASSIGNMENT_IS_NOT_REPORTED_AS_HAVING_NONE()
    {
        // ★★ THE HONESTY RULE, AND IT IS NOW A FACT RATHER THAN A FORM OF WORDS. The payee HAS an
        // assignment; this caller may not read it. ListAssignmentsByPayeeHandler answers that denial
        // with an EMPTY PAGE rather than an error, so what reaches the tool is indistinguishable from a
        // payee on no plan — which is why the tool asks the guard directly and reports a DIFFERENT
        // outcome. Before that, one token covered both and the model resolved it to the confident
        // reading: a user was told their payee had no plans while the payee screen listed one.
        var h = Build(nameof(A_PAYEE_WITH_NO_VISIBLE_ASSIGNMENT_IS_NOT_REPORTED_AS_HAVING_NONE),
            Guid.NewGuid(), PayeeVisibility.None, AssignmentPermissions);
        var payeeId = SeedPayee(h, "Ana García", "EMP-ANA");
        SeedAssignment(h, payeeId, SeedPlan(h, "Q3 2026 — EMEA"), "Ana García", "EMP-ANA");

        var payload = await ByNameAsync(h, "Ana García");

        payload.GetProperty("outcome").GetString().Should().Be("AssignmentsNotVisible");
        payload.TryGetProperty("assignments", out _).Should().BeFalse(
            "an empty list under the ordinary outcome is what the model would read as 'has no plans'");
        payload.GetProperty("message").GetString().Should()
            .Contain("MAY NOT READ THEIR PLAN ASSIGNMENTS").And
            .Contain("never that there are none");

        // ★ NO includedEnded HERE. Nothing was filtered because nothing was queried, and reporting a
        // filter that never ran is how "no ACTIVE ones" gets said about data nobody looked at.
        payload.TryGetProperty("includedEnded", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_payee_who_genuinely_has_no_assignment_takes_a_DIFFERENT_outcome()
    {
        // ★★ THE WHOLE WORK ITEM IN ONE ASSERTION, read against the test above it. Same empty page,
        // opposite fact: there, the assignments were hidden; here, this caller can see everything and
        // there is genuinely nothing. One token for both made the assistant unable to be honest no
        // matter how the prompt was worded, because the data to be honest WITH was not there.
        var h = Build(nameof(A_payee_who_genuinely_has_no_assignment_takes_a_DIFFERENT_outcome),
            Guid.NewGuid(), null, AssignmentPermissions);
        SeedPayee(h, "Ana García", "EMP-ANA");

        var payload = await ByNameAsync(h, "Ana García");

        payload.GetProperty("outcome").GetString().Should().Be("NoAssignments");
        payload.GetProperty("found").GetBoolean().Should().BeTrue(
            "the PAYEE was found — only their assignments were not");

        // The model is told it MAY say this one out loud, which is exactly what it may not do for
        // AssignmentsNotVisible. The two messages must never converge again.
        payload.GetProperty("message").GetString().Should().Contain("CAN see");
    }

    [Fact]
    public async Task THE_EMPTY_ANSWER_SAYS_WHO_IT_SEARCHED_FOR_code_included()
    {
        // ★ AN ANSWER THAT FOUND NOTHING HAS TO SAY WHAT IT LOOKED FOR. "I could not see any
        // assignment" is unfalsifiable on its own; "I looked for Ana García (EMP-ANA) and could not see
        // any assignment" lets the user catch a lookup that went to the wrong person — which is
        // precisely the failure that produced this work item. The code cannot come off the assignment
        // rows: there are none.
        var h = Build(nameof(THE_EMPTY_ANSWER_SAYS_WHO_IT_SEARCHED_FOR_code_included),
            Guid.NewGuid(), null, AssignmentPermissions);
        SeedPayee(h, "Ana García", "EMP-ANA");

        var byName = await ByNameAsync(h, "Ana García");
        byName.GetProperty("payeeName").GetString().Should().Be("Ana García");
        byName.GetProperty("payeeEmployeeCode").GetString().Should().Be("EMP-ANA");
        byName.GetProperty("matchedBy").GetString().Should().Be("ExactName");

        // And on the restricted path too, where naming the person is what makes "go and look at their
        // screen" an actionable answer rather than a shrug.
        var restricted = Build(nameof(THE_EMPTY_ANSWER_SAYS_WHO_IT_SEARCHED_FOR_code_included) + ".r",
            Guid.NewGuid(), PayeeVisibility.None, AssignmentPermissions);
        SeedPayee(restricted, "Ana García", "EMP-ANA");

        var hidden = await ByNameAsync(restricted, "Ana García");
        hidden.GetProperty("outcome").GetString().Should().Be("AssignmentsNotVisible");
        hidden.GetProperty("payeeEmployeeCode").GetString().Should().Be("EMP-ANA");
    }

    [Fact]
    public async Task THE_ASSIGNMENT_THE_PAYEE_SCREEN_SHOWS_IS_THE_ONE_THE_TOOL_RETURNS()
    {
        // ★★ THE CONTRADICTION THAT STARTED THIS. The payee screen listed "Q3 2026 — Plan
        // Comercial EMEA", running Jul 1 to Sep 30 and current today, while the assistant said it could
        // find no assignment. Both surfaces go through the SAME query and the SAME handler
        // (PayeesController.ListAssignments and this tool both send ListAssignmentsByPayeeQuery), and
        // the tool's scope is the WIDER of the two: the screen sends a period, the tool sends none. So
        // an assignment the screen can show can never be one the tool cannot — unless visibility
        // differs, which is now reported instead of swallowed.
        var h = Build(nameof(THE_ASSIGNMENT_THE_PAYEE_SCREEN_SHOWS_IS_THE_ONE_THE_TOOL_RETURNS),
            Guid.NewGuid(), null, AssignmentPermissions);
        var payeeId = SeedPayee(h, "Rudolph Chipellin", "CEO-001");
        SeedAssignment(h, payeeId, SeedPlan(h, "Q3 2026 — Plan Comercial EMEA"),
            "Rudolph Chipellin", "CEO-001");

        // What the screen asks for: this month, active, newest first.
        var screen = await new ListAssignmentsByPayeeHandler(
                h.Db, new RoleAuthorization(AssignmentPermissions),
                new FakePayeeAccessGuard(PayeeVisibility.Everything), new FakeClock(Now.UtcDateTime))
            .Handle(
                new ListAssignmentsByPayeeQuery(payeeId, new PaginationQuery
                {
                    Page = 1, PageSize = 10, SortBy = "effectivestart", SortOrder = "desc",
                    Period = "this-month",
                }),
                default);

        screen.Value!.Items.Should().ContainSingle("this is what the card renders");

        var payload = await ByIdAsync(h, payeeId);

        payload.GetProperty("outcome").GetString().Should().Be("PayeePlans");
        payload.GetProperty("assignments").EnumerateArray().Single()
            .GetProperty("planName").GetString().Should().Be("Q3 2026 — Plan Comercial EMEA");
    }

    [Fact]
    public async Task On_the_id_path_an_empty_answer_STILL_NAMES_THE_PERSON_it_is_about()
    {
        // ★ WHY THE ID PATH CONFIRMS THE PAYEE FIRST. With no assignment rows there is no name to
        // recover from them, and an empty answer that cannot say WHO it concerns is unusable in a
        // follow-up ("I see no assignments for… whom?"). GetPayeeByIdQuery supplies the canonical name
        // — and, more importantly, is what stops this branch asserting `found: true` about an id that
        // belongs to nobody the caller may see.
        var h = Build(nameof(On_the_id_path_an_empty_answer_STILL_NAMES_THE_PERSON_it_is_about),
            Guid.NewGuid(), null, AssignmentPermissions);
        var payeeId = SeedPayee(h, "Ana García", "EMP-ANA");

        var payload = await ByIdAsync(h, payeeId);

        payload.GetProperty("outcome").GetString().Should().Be("NoAssignments");
        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("payeeName").GetString().Should().Be("Ana García");
    }

    [Fact]
    public async Task Deactivated_assignments_are_hidden_by_default_and_returned_on_request()
    {
        // "What plans is Ana on" means now. Answering with an assignment that ended reads as a current
        // plan — the same reason the handler's own contract defaults to Active.
        var h = Build(nameof(Deactivated_assignments_are_hidden_by_default_and_returned_on_request),
            Guid.NewGuid(), null, AssignmentPermissions);
        var payeeId = SeedPayee(h, "Ana García", "EMP-ANA");
        SeedAssignment(h, payeeId, SeedPlan(h, "Antiguo Plan"), "Ana García", "EMP-ANA", deactivated: true);

        var byDefault = await ByNameAsync(h, "Ana García");
        byDefault.GetProperty("outcome").GetString().Should().Be("NoAssignments");
        byDefault.GetProperty("includedEnded").GetBoolean().Should().BeFalse();
        // ★ AND THE MESSAGE STILL REFUSES THE BIGGER CLAIM. They DO have an assignment; it ended. The
        // model may say "no active plan" and must not say "never had one" — includedEnded is what
        // separates the two, so the payload spells the difference out rather than trusting the flag.
        byDefault.GetProperty("message").GetString().Should()
            .Contain("NOT that they never had one");

        var withEnded = await RunAsync(h, """{"payeeName":"Ana García","includeEnded":true}""");
        withEnded.GetProperty("outcome").GetString().Should().Be("PayeePlans");
        withEnded.GetProperty("assignments").EnumerateArray().Single()
            .GetProperty("assignmentStatus").GetString().Should().Be("Deactivated");
    }

    // ══ ISOLATION AND REFUSAL ═════════════════════════════════════════════════

    [Fact]
    public async Task ANOTHER_TENANTS_PAYEE_ID_IS_REFUSED_INDISTINGUISHABLY()
    {
        // ★ REUSING AN ID FROM THE CONVERSATION DOES NOT BUY ACCESS. The id path skips name resolution,
        // never the guard — an id from anywhere at all takes the same refusal as a payee who does not
        // exist. Two harnesses over ONE database, separated only by the live tenant filter.
        var dbName = nameof(ANOTHER_TENANTS_PAYEE_ID_IS_REFUSED_INDISTINGUISHABLY);
        var theirs = Build(dbName, Guid.NewGuid(), null, AssignmentPermissions);
        var payeeId = SeedPayee(theirs, "Ana García", "EMP-ANA");
        SeedAssignment(theirs, payeeId, SeedPlan(theirs, "Q3 2026 — EMEA"), "Ana García", "EMP-ANA");

        var mine = Build(dbName, Guid.NewGuid(), null, AssignmentPermissions);

        var byId = await ByIdAsync(mine, payeeId);
        var byName = await ByNameAsync(mine, "Ana García");

        byId.GetProperty("found").GetBoolean().Should().BeFalse();
        byName.GetProperty("outcome").GetString().Should().Be("NotFoundOrNotVisible");
        byName.GetProperty("message").GetString().Should().Be(GetPayeePlansTool.RefusalMessage);
    }

    [Fact]
    public async Task Every_refusal_is_the_same_sentence()
    {
        // "No such payee", "not yours" and "you may not read assignments" must be one payload: telling
        // them apart confirms the record exists, which is the fact worth fishing for.
        var h = Build(nameof(Every_refusal_is_the_same_sentence), Guid.NewGuid(), null, AssignmentPermissions);
        SeedPayee(h, "Ana García", "EMP-ANA");

        var unknown = await ByNameAsync(h, "Nobody At All");
        unknown.GetProperty("found").GetBoolean().Should().BeFalse();
        unknown.GetProperty("message").GetString().Should().Be(GetPayeePlansTool.RefusalMessage);

        // Cannot list payees at all.
        var blind = Build(nameof(Every_refusal_is_the_same_sentence) + ".blind", Guid.NewGuid());
        var refused = await ByNameAsync(blind, "Ana García");
        refused.GetProperty("message").GetString().Should().Be(GetPayeePlansTool.RefusalMessage);
    }

    [Fact]
    public async Task Missing_AssignmentsRead_refuses_rather_than_reporting_no_plans()
    {
        var h = Build(nameof(Missing_AssignmentsRead_refuses_rather_than_reporting_no_plans),
            Guid.NewGuid(), null, Permission.PayeesRead);
        var payeeId = SeedPayee(h, "Ana García", "EMP-ANA");
        SeedAssignment(h, payeeId, SeedPlan(h, "Q3 2026 — EMEA"), "Ana García", "EMP-ANA");

        var payload = await ByNameAsync(h, "Ana García");

        payload.GetProperty("outcome").GetString().Should().Be("NotFoundOrNotVisible",
            "a missing permission is a refusal, not an assertion that the person has no plan");
    }

    [Fact]
    public async Task An_ambiguous_name_refuses_rather_than_picking_one()
    {
        // Two people called Ana García is an ordinary state of a company; choosing one would put the
        // wrong person's compensation on screen.
        var h = Build(nameof(An_ambiguous_name_refuses_rather_than_picking_one),
            Guid.NewGuid(), null, AssignmentPermissions);
        SeedPayee(h, "Ana García", "EMP-ANA1");
        SeedPayee(h, "Ana García", "EMP-ANA2");

        (await ByNameAsync(h, "Ana García")).GetProperty("found").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_BROKEN_LOOKUP_IS_RAISED_NOT_ANSWERED_AS_NOT_FOUND()
    {
        // ★ A FAILED QUERY IS NOT AN EMPTY ONE. Folding the two together answers a broken lookup with a
        // lie about the user's own data; raised, the runner fails the turn and the user gets a retry.
        var h = Build(nameof(A_BROKEN_LOOKUP_IS_RAISED_NOT_ANSWERED_AS_NOT_FOUND),
            Guid.NewGuid(), null, AssignmentPermissions);
        SeedPayee(h, "Ana García", "EMP-ANA");
        h.Sender.FailPayeeListQuery = true;

        await h.Tool.Invoking(t => t.RunAsync("""{"payeeName":"Ana García"}""", default))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Unreadable_arguments_refuse_without_a_lookup()
    {
        var h = Build(nameof(Unreadable_arguments_refuse_without_a_lookup),
            Guid.NewGuid(), null, AssignmentPermissions);

        (await RunAsync(h, "not json")).GetProperty("found").GetBoolean().Should().BeFalse();
        (await RunAsync(h, "{}")).GetProperty("found").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task An_unparseable_id_falls_through_to_the_name()
    {
        // The model occasionally writes a placeholder; the name it also sent is a good second chance.
        var h = Build(nameof(An_unparseable_id_falls_through_to_the_name),
            Guid.NewGuid(), null, AssignmentPermissions);
        var payeeId = SeedPayee(h, "Ana García", "EMP-ANA");
        SeedAssignment(h, payeeId, SeedPlan(h, "Q3 2026 — EMEA"), "Ana García", "EMP-ANA");

        var payload = await RunAsync(h, """{"payeeId":"<uuid>","payeeName":"Ana García"}""");

        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("payeeId").GetGuid().Should().Be(payeeId);
    }

    // ══ SINGLE RESPONSIBILITY ═════════════════════════════════════════════════

    [Fact]
    public async Task THE_PAYLOAD_CARRIES_NO_MONEY_AT_ALL()
    {
        // ★ Enforced by omission. An assignment says WHICH plan, never what it paid — a payload that
        // grew to cover the follow-up would be this tool becoming the two it sits between, and a rate
        // reported here would be one nothing computed.
        var h = Build(nameof(THE_PAYLOAD_CARRIES_NO_MONEY_AT_ALL), Guid.NewGuid(), null, AssignmentPermissions);
        var payeeId = SeedPayee(h, "Ana García", "EMP-ANA");
        SeedAssignment(h, payeeId, SeedPlan(h, "Q3 2026 — EMEA"), "Ana García", "EMP-ANA");

        var raw = await h.Tool.RunAsync("""{"payeeName":"Ana García"}""", default);

        foreach (var forbidden in new[]
                 {
                     "rate", "commission", "amount", "currency", "earned", "balance", "payout",
                 })
        {
            raw.ToLowerInvariant().Should().NotContain(forbidden,
                "this tool reports the assignment and nothing that could be read as pay");
        }
    }
}
