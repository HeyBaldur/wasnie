using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.Tools;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Assignments;
using Wasnie.Application.Compensation.Handlers.Ledger;
using Wasnie.Application.Compensation.Handlers.Payees;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Application.Compensation.Queries.Ledger;
using Wasnie.Application.Compensation.Queries.Payees;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// TWO PEOPLE, ONE NAME — and for a long time that came back as nobody.
///
/// ★ THE FAILURE THIS SUITE PINS DOWN, AND IT WAS A TRUST FAILURE. A finance analyst asked the assistant
/// for Anna Schmidt's balance and was told there was no record of her, while her row was on the screen in
/// front of them. The resolver had done the right thing — this tenant holds TWO Anna Schmidts (EPO9006,
/// terminated; EMP406, active) and choosing one of them would have put the wrong person's pay on screen —
/// but its refusal came back as <c>null</c> and landed in the branch that means "no such payee".
///
/// ★ THE LOG TOLD THE SAME LIE, WHICH IS WHY THIS IS TESTED TOO. The ambiguity was recorded as
/// <c>NotFound</c>, so the first investigation went hunting for a status filter excluding terminated
/// employees. No such filter exists anywhere in the resolution path, and none was ever the cause — 11 of
/// the tenant's 12 duplicated names are entirely active and fail identically. A cause that misreports the
/// reason costs a whole diagnosis, so <c>AmbiguousPayee</c> is asserted in the log as well as the payload.
///
/// ★ THE GUARDS ARE NOT MOCKED, as in the other tool suites: real handlers, real DbContext, real tenant
/// filter, permissions answered from an actual role.
/// </summary>
public sealed class AssistantAmbiguousPayeeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

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

    /// <summary>
    /// Keeps every line the tool logged, so the cause can be asserted.
    ///
    /// ★ THE POINT IS NOT THAT SOMETHING WAS LOGGED. It is that the word "NotFound" does NOT appear when
    /// several people were found — that word is what sent the original diagnosis down the wrong path.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }

    private sealed class HandlerSender(
        IApplicationDbContext db,
        IAuthorizationService auth,
        IPayeeAccessGuard guard,
        ITenantContext tenantContext) : ISender
    {
        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            switch (request)
            {
                case ListPayeesQuery q:
                    return (TResponse)(object)await new ListPayeesHandler(db, tenantContext, auth)
                        .Handle(q, cancellationToken);

                case GetPayeeByIdQuery q:
                    return (TResponse)(object)await new GetPayeeByIdHandler(db, auth)
                        .Handle(q, cancellationToken);

                case GetPayeeLedgerSummaryQuery q:
                    return (TResponse)(object)await new GetPayeeLedgerSummaryHandler(
                        db, auth, guard, new FakeClock(Now.UtcDateTime)).Handle(q, cancellationToken);

                case ListAssignmentsByPayeeQuery q:
                    return (TResponse)(object)await new ListAssignmentsByPayeeHandler(
                        db, auth, guard, new FakeClock(Now.UtcDateTime)).Handle(q, cancellationToken);

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
        ApplicationDbContext Db,
        GetPayeeLedgerSummaryTool Balance,
        GetPayeePlansTool Plans,
        CapturingLogger<GetPayeeLedgerSummaryTool> BalanceLog,
        Guid TenantId);

    /// <summary>Everything both payee tools need, and nothing else.</summary>
    private static readonly string[] Permissions =
        [Permission.PayeesRead, Permission.LedgerSummaryRead, Permission.AssignmentsRead];

    private static Harness Build(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(AssistantAmbiguousPayeeTests)}.{dbName}")
                .Options,
            tenantCtx, Substitute.For<IPublisher>());

        var sender = new HandlerSender(
            db, new RoleAuthorization(Permissions),
            new FakePayeeAccessGuard(PayeeVisibility.Everything), tenantCtx);

        var balanceLog = new CapturingLogger<GetPayeeLedgerSummaryTool>();

        return new Harness(
            db,
            new GetPayeeLedgerSummaryTool(sender, balanceLog),
            new GetPayeePlansTool(
                sender, FakePayeeAccessGuard.SeesEverything(),
                new CapturingLogger<GetPayeePlansTool>()),
            balanceLog,
            tenantId);
    }

    private static void SeedPayee(Harness h, string fullName, string code, bool terminated = false)
    {
        var payee = Payee.Create(h.TenantId, fullName, code, $"{code}@test.com".ToLowerInvariant(),
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);

        if (terminated)
            payee.MarkAsTerminated(new DateOnly(2026, 6, 30), "test", Now);

        h.Db.Payees.Add(payee);
        h.Db.SaveChanges();
    }

    /// <summary>The reproduced case: two Anna Schmidts, one gone and one still here.</summary>
    private static void SeedTwoAnnas(Harness h)
    {
        SeedPayee(h, "Anna Schmidt", "EPO9006", terminated: true);
        SeedPayee(h, "Anna Schmidt", "EMP406");
    }

    private static async Task<JsonElement> BalanceAsync(Harness h, string name) =>
        JsonDocument.Parse(
            await h.Balance.RunAsync($$"""{"payeeName":"{{name}}"}""", default)).RootElement;

    private static async Task<JsonElement> PlansAsync(Harness h, string name) =>
        JsonDocument.Parse(
            await h.Plans.RunAsync($$"""{"payeeName":"{{name}}"}""", default)).RootElement;

    private static string? Outcome(JsonElement payload) =>
        payload.TryGetProperty("outcome", out var o) ? o.GetString() : null;

    // ══ 1. SEVERAL PEOPLE ════════════════════════════════════════════════════

    [Fact]
    public async Task A_name_shared_by_two_payees_is_ambiguous_and_not_a_refusal()
    {
        var h = Build(nameof(A_name_shared_by_two_payees_is_ambiguous_and_not_a_refusal));
        SeedTwoAnnas(h);

        var payload = await BalanceAsync(h, "Anna Schmidt");

        Outcome(payload).Should().Be(PayeeAmbiguity.Outcome);
        payload.GetProperty("found").GetBoolean().Should().BeFalse();
        payload.GetProperty("candidateCount").GetInt32().Should().Be(2);
        payload.GetProperty("requestedName").GetString().Should().Be("Anna Schmidt");
    }

    [Fact]
    public async Task The_candidates_carry_the_name_the_code_and_the_status()
    {
        var h = Build(nameof(The_candidates_carry_the_name_the_code_and_the_status));
        SeedTwoAnnas(h);

        var candidates = (await BalanceAsync(h, "Anna Schmidt"))
            .GetProperty("candidates")
            .EnumerateArray()
            .Select(c => (
                Name: c.GetProperty("fullName").GetString(),
                Code: c.GetProperty("employeeCode").GetString(),
                Status: c.GetProperty("status").GetString()))
            .ToList();

        candidates.Should().HaveCount(2);
        candidates.Should().AllSatisfy(c => c.Name.Should().Be("Anna Schmidt"));

        // ★ THE STATUS IS THE FIELD THE ANALYST ACTUALLY CHOOSES ON. Two identical names and two opaque
        // codes are not a choice anyone can make; "terminated" versus "active" is.
        candidates.Should().ContainSingle(c => c.Code == "EPO9006" && c.Status == "Terminated");
        candidates.Should().ContainSingle(c => c.Code == "EMP406" && c.Status == "Active");
    }

    [Fact]
    public async Task An_ambiguous_answer_carries_no_money_for_anybody()
    {
        var h = Build(nameof(An_ambiguous_answer_carries_no_money_for_anybody));
        SeedTwoAnnas(h);

        var raw = await h.Balance.RunAsync("""{"payeeName":"Anna Schmidt"}""", default);

        // Nothing was read for either of them, so no balance field may appear — a figure here would
        // belong to a person the user has not yet chosen.
        raw.Should().NotContain("earnedCommissions")
            .And.NotContain("netPendingPayout")
            .And.NotContain("outstandingDebt");
    }

    // ══ 2. ONE PERSON — UNCHANGED ════════════════════════════════════════════

    [Fact]
    public async Task A_unique_name_still_resolves_exactly_as_before()
    {
        var h = Build(nameof(A_unique_name_still_resolves_exactly_as_before));
        SeedTwoAnnas(h);
        SeedPayee(h, "Tony Stark", "EMP-654", terminated: true);

        var payload = await BalanceAsync(h, "Tony Stark");

        // ★ AND HE IS TERMINATED ON PURPOSE. The first hypothesis was that terminated payees are
        // filtered out of the assistant's search. They are not, and this is the test that says so: a
        // unique name resolves whether or not the person still works here.
        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("payeeName").GetString().Should().Be("Tony Stark");
        Outcome(payload).Should().NotBe(PayeeAmbiguity.Outcome);
    }

    // ══ 3. NOBODY — STILL A REFUSAL, AND A DIFFERENT ONE ═════════════════════

    [Fact]
    public async Task A_name_belonging_to_nobody_is_still_not_found_and_not_ambiguous()
    {
        var h = Build(nameof(A_name_belonging_to_nobody_is_still_not_found_and_not_ambiguous));
        SeedTwoAnnas(h);

        var payload = await BalanceAsync(h, "Zoe Nobody");

        payload.GetProperty("found").GetBoolean().Should().BeFalse();
        Outcome(payload).Should().NotBe(PayeeAmbiguity.Outcome);
        payload.TryGetProperty("candidates", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Sharing_a_word_with_a_name_that_exists_is_not_ambiguity()
    {
        var h = Build(nameof(Sharing_a_word_with_a_name_that_exists_is_not_ambiguity));
        SeedTwoAnnas(h);

        // ★ THE BOUNDARY OF THE FEATURE. "Zoe Schmidt" fetches both Annas as candidate ROWS, because the
        // database is narrowed by the longest token. Neither of them is a person the user might have
        // meant — they merely share a surname with a name that does not exist — and offering them would
        // be the resolver guessing out loud. Ambiguity is reserved for a name that genuinely belongs to
        // more than one person.
        var payload = await BalanceAsync(h, "Zoe Schmidt");

        Outcome(payload).Should().NotBe(PayeeAmbiguity.Outcome);
        payload.GetProperty("found").GetBoolean().Should().BeFalse();
    }

    // ══ 4. THE WAY OUT: THE EMPLOYEE CODE ════════════════════════════════════

    [Fact]
    public async Task The_employee_code_the_user_replies_with_resolves_one_of_them()
    {
        var h = Build(nameof(The_employee_code_the_user_replies_with_resolves_one_of_them));
        SeedTwoAnnas(h);

        // The whole conversation this fix enables: ask → "which of these two?" → answer with the code.
        (await BalanceAsync(h, "Anna Schmidt")).Pipe(p => Outcome(p).Should().Be(PayeeAmbiguity.Outcome));

        var payload = await BalanceAsync(h, "EPO9006");

        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("matchedBy").GetString().Should().Be(nameof(PayeeMatch.EmployeeCode));
        payload.GetProperty("payeeName").GetString().Should().Be("Anna Schmidt");
    }

    // ══ 5. THE LOG ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task The_log_says_ambiguous_and_never_says_not_found()
    {
        var h = Build(nameof(The_log_says_ambiguous_and_never_says_not_found));
        SeedTwoAnnas(h);

        await BalanceAsync(h, "Anna Schmidt");

        h.BalanceLog.Lines.Should().ContainMatch($"*{nameof(AssistantToolCause.AmbiguousPayee)}*");

        // ★ THE ASSERTION THAT COST A DIAGNOSIS. "NotFound" in this log is what sent the investigation
        // looking for a status filter that has never existed.
        h.BalanceLog.Lines.Should().NotContainMatch($"*{nameof(AssistantToolCause.NotFound)}*");
    }

    // ══ BOTH TOOLS AGREE ═════════════════════════════════════════════════════

    [Fact]
    public async Task The_assignments_tool_answers_the_same_ambiguity_the_same_way()
    {
        var h = Build(nameof(The_assignments_tool_answers_the_same_ambiguity_the_same_way));
        SeedTwoAnnas(h);

        // ★ ADJACENT TURNS MUST NOT DISAGREE. "Which Anna?" for a balance and "she does not exist" for
        // her plans, in one conversation, would be worse than the bug being fixed.
        var balance = await h.Balance.RunAsync("""{"payeeName":"Anna Schmidt"}""", default);
        var plans = await h.Plans.RunAsync("""{"payeeName":"Anna Schmidt"}""", default);

        plans.Should().Be(balance);
    }
}

internal static class PipeExtensions
{
    /// <summary>Assert on a value inline without naming it. Test-only sugar.</summary>
    public static void Pipe<T>(this T value, Action<T> assert) => assert(value);
}
