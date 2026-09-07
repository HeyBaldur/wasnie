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

    // ══ 3b. PART OF A NAME, BORNE BY SEVERAL PEOPLE ══════════════════════════

    /// <summary>The second reported tenant: three people a user can reasonably call "Camille".</summary>
    private static void SeedThreeCamilles(Harness h)
    {
        SeedPayee(h, "Camille Laurent", "EPO9009", terminated: true);
        SeedPayee(h, "Camille Laurent", "EMP409");
        SeedPayee(h, "Camille Martin", "FR-301");
    }

    /// <summary>
    /// ★★ THE DEFECT AS REPORTED, AND IT IS THE ANNA FAILURE ARRIVING BY THE OTHER ROAD. A tenant holds
    /// Camille Laurent (EPO9009), Camille Laurent (EMP409) and Camille Martin (FR-301). Asked for
    /// "Camille", the assistant answered that it could not locate ANY payee by that name.
    ///
    /// No full name IS "Camille", so the exact pass found nothing; three rows came back from the
    /// substring search, so the "exactly one candidate" branch did not fire; and the resolver fell
    /// through to NOT FOUND. Three people on the user's screen, reported as zero — which is the single
    /// worst answer this payload exists to prevent, and rule 23 could not help because the outcome
    /// never reached it.
    /// </summary>
    [Fact]
    public async Task A_first_name_borne_by_three_payees_is_ambiguous_and_never_not_found()
    {
        var h = Build(nameof(A_first_name_borne_by_three_payees_is_ambiguous_and_never_not_found));
        SeedThreeCamilles(h);

        var payload = await BalanceAsync(h, "Camille");

        Outcome(payload).Should().Be(PayeeAmbiguity.Outcome);
        payload.GetProperty("found").GetBoolean().Should().BeFalse();
        payload.GetProperty("candidateCount").GetInt32().Should().Be(3);
    }

    /// <summary>
    /// ★ THE FULL NAMES ARE THE POINT WHEN THE SHARED PART IS ONLY A FIRST NAME. Two of these people
    /// are namesakes and the third is not, so a list that echoed back "Camille" three times would hide
    /// the one distinction the user can actually choose on. The payload has to carry Laurent and
    /// Martin.
    /// </summary>
    [Fact]
    public async Task The_candidates_of_a_partial_name_carry_their_own_full_names()
    {
        var h = Build(nameof(The_candidates_of_a_partial_name_carry_their_own_full_names));
        SeedThreeCamilles(h);

        var candidates = (await BalanceAsync(h, "Camille"))
            .GetProperty("candidates")
            .EnumerateArray()
            .Select(c => (
                Name: c.GetProperty("fullName").GetString(),
                Code: c.GetProperty("employeeCode").GetString()))
            .ToList();

        candidates.Should().HaveCount(3);
        candidates.Should().ContainSingle(c => c.Name == "Camille Laurent" && c.Code == "EPO9009");
        candidates.Should().ContainSingle(c => c.Name == "Camille Laurent" && c.Code == "EMP409");
        candidates.Should().ContainSingle(c => c.Name == "Camille Martin" && c.Code == "FR-301");
    }

    /// <summary>
    /// ★ A SURNAME BEHAVES THE SAME WAY, and this is the case that scales: in a real tenant "García"
    /// belongs to fifteen people, and the answer is fifteen people to choose from rather than none.
    /// </summary>
    [Fact]
    public async Task A_surname_borne_by_several_payees_is_ambiguous_too()
    {
        var h = Build(nameof(A_surname_borne_by_several_payees_is_ambiguous_too));
        SeedThreeCamilles(h);

        var payload = await BalanceAsync(h, "Laurent");

        Outcome(payload).Should().Be(PayeeAmbiguity.Outcome);
        payload.GetProperty("candidateCount").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// ★★ AND ONE BEARER IS STILL AN ANSWER, NOT A MENU. This is the half that must not regress: a
    /// question with exactly one possible subject has to be answered, never turned into a form. The
    /// resolver says so in `matchedBy`, which rule 19a uses to open with the person's full name.
    /// </summary>
    [Fact]
    public async Task A_first_name_borne_by_ONE_payee_still_resolves_straight_to_them()
    {
        var h = Build(nameof(A_first_name_borne_by_ONE_payee_still_resolves_straight_to_them));
        SeedPayee(h, "Camille Martin", "FR-301");

        var payload = await BalanceAsync(h, "Camille");

        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("payeeName").GetString().Should().Be("Camille Martin");
        payload.GetProperty("matchedBy").GetString()
            .Should().Be(nameof(PayeeMatch.PartialNameSingleCandidate));
    }

    /// <summary>★ Both payee tools must agree about a partial name exactly as they do about a full one.</summary>
    [Fact]
    public async Task Both_tools_agree_on_a_partial_name_borne_by_several()
    {
        var h = Build(nameof(Both_tools_agree_on_a_partial_name_borne_by_several));
        SeedThreeCamilles(h);

        await AssertSameAmbiguity(h, "Camille");
    }

    // ══ 3c. THE FORM THE USER ACTUALLY SEES ══════════════════════════════════

    /// <summary>
    /// ★★ THE WHOLE POINT, END TO END: the payload the tool returns carries a clarify FORM, one option
    /// per real person, built from the rows the lookup matched. Nothing about this depends on the
    /// model — no prompt can fail to trigger it and no sampling can vary it, which is what the
    /// consistency complaint asked for.
    /// </summary>
    [Fact]
    public async Task The_ambiguous_payload_carries_a_form_with_one_option_per_person()
    {
        var h = Build(nameof(The_ambiguous_payload_carries_a_form_with_one_option_per_person));
        SeedThreeCamilles(h);

        var form = AssistantClarify.Extract(await h.Balance.RunAsync(
            """{"payeeName":"Camille"}""", default));

        form.Should().NotBeNull();
        form!.IsEntityForm.Should().BeTrue();
        form.Options.Should().HaveCount(3);

        // ★ THE ARGUMENT IS THE EMPLOYEE CODE, NOT THE NAME. Pressing an option has to resolve to
        // exactly one person, and the name is the very thing that did not.
        form.Options.Select(o => o.Argument)
            .Should().BeEquivalentTo(["EPO9009", "EMP409", "FR-301"]);
    }

    /// <summary>
    /// ★ EACH OPTION IS DISTINGUISHABLE. Two of these three share a full name, so name alone offers a
    /// choice nobody can make; the code separates the namesakes and the status is usually what the
    /// reader actually knows about the person they mean.
    /// </summary>
    [Fact]
    public async Task Every_option_carries_the_full_name_the_code_and_the_status()
    {
        var h = Build(nameof(Every_option_carries_the_full_name_the_code_and_the_status));
        SeedThreeCamilles(h);

        var entities = AssistantClarify
            .Extract(await h.Balance.RunAsync("""{"payeeName":"Camille"}""", default))!
            .Options.Select(o => o.Entity!)
            .ToList();

        entities.Should().ContainSingle(e =>
            e.Name == "Camille Laurent" && e.Code == "EPO9009" && e.Status == "Terminated");
        entities.Should().ContainSingle(e =>
            e.Name == "Camille Laurent" && e.Code == "EMP409" && e.Status == "Active");
        entities.Should().ContainSingle(e =>
            e.Name == "Camille Martin" && e.Code == "FR-301" && e.Status == "Active");
    }

    /// <summary>
    /// ★★ THE FORM OFFERS THE FUNCTION THE USER ASKED FOR, NOT THE OTHER ONE. Somebody who asked about
    /// assignments and pressed a name must get assignments — a form that quietly switched them to a
    /// balance would answer a question they did not ask, about the person they did choose.
    /// </summary>
    [Fact]
    public async Task The_form_reruns_the_lookup_that_hit_the_ambiguity()
    {
        var h = Build(nameof(The_form_reruns_the_lookup_that_hit_the_ambiguity));
        SeedThreeCamilles(h);

        var balance = AssistantClarify.Extract(
            await h.Balance.RunAsync("""{"payeeName":"Camille"}""", default))!;
        var plans = AssistantClarify.Extract(
            await h.Plans.RunAsync("""{"payeeName":"Camille"}""", default))!;

        balance.Options.Should().AllSatisfy(o =>
            o.Function.Should().Be(GetPayeeLedgerSummaryTool.ToolName));
        plans.Options.Should().AllSatisfy(o =>
            o.Function.Should().Be(GetPayeePlansTool.ToolName));
    }

    /// <summary>★ A single match resolves, so there is no form to show.</summary>
    [Fact]
    public async Task A_resolved_lookup_carries_no_form()
    {
        var h = Build(nameof(A_resolved_lookup_carries_no_form));
        SeedPayee(h, "Camille Martin", "FR-301");

        AssistantClarify.Extract(await h.Balance.RunAsync("""{"payeeName":"Camille"}""", default))
            .Should().BeNull();
    }

    // ══ 3d. THE CODE ARRIVING IN THE WRONG FIELD ═════════════════════════════

    /// <summary>
    /// ★★ THE REPORTED CONTRADICTION, REPRODUCED. The clarify form offered "Camille Laurent · EPO9009",
    /// the user picked it, and the next turn answered that no payee with code EPO9009 exists — about a
    /// code the assistant itself had just printed.
    ///
    /// ★ AND THE CAUSE WAS NOT THE SEARCH. The runtime log said <c>UnreadableArguments</c>, not
    /// <c>NotFound</c>: the dispatcher put the code in `payeeId` (its own rule tells it to send the id
    /// and NOT the name), `Guid.TryParse` rejected it, and with no `payeeName` beside it the tool
    /// refused BEFORE the resolver ran. The employee-code branch was never reached — it has always
    /// worked, and it is tested below.
    /// </summary>
    [Fact]
    public async Task An_employee_code_sent_as_the_ID_still_resolves_the_payee()
    {
        var h = Build(nameof(An_employee_code_sent_as_the_ID_still_resolves_the_payee));
        SeedThreeCamilles(h);

        // Exactly the shape the dispatcher produced: the code, in the id field, and nothing else.
        var payload = JsonDocument.Parse(
            await h.Balance.RunAsync("""{"payeeId":"EPO9009"}""", default)).RootElement;

        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("payeeName").GetString().Should().Be("Camille Laurent");
    }

    /// <summary>★ The assignments tool reached the same dead end and is salvaged the same way.</summary>
    [Fact]
    public async Task The_assignments_tool_also_accepts_a_code_sent_as_the_ID()
    {
        var h = Build(nameof(The_assignments_tool_also_accepts_a_code_sent_as_the_ID));
        SeedThreeCamilles(h);

        var payload = JsonDocument.Parse(
            await h.Plans.RunAsync("""{"payeeId":"FR-301"}""", default)).RootElement;

        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("payeeName").GetString().Should().Be("Camille Martin");
    }

    /// <summary>
    /// ★★ A REAL NAME STILL WINS. The salvage only fills a gap: a turn carrying both a usable name and
    /// a junk id must behave exactly as it did before, or this fix would start overriding good input
    /// with bad.
    /// </summary>
    [Fact]
    public async Task A_real_name_is_not_overridden_by_a_junk_id()
    {
        var h = Build(nameof(A_real_name_is_not_overridden_by_a_junk_id));
        SeedThreeCamilles(h);

        var payload = JsonDocument.Parse(
            await h.Balance.RunAsync("""{"payeeId":"EPO9009","payeeName":"Camille Martin"}""", default))
            .RootElement;

        payload.GetProperty("payeeName").GetString().Should().Be("Camille Martin");
    }

    /// <summary>
    /// ★★ A PLACEHOLDER IS NOT AN IDENTIFIER. A model with no id sometimes writes one of these rather
    /// than omitting the field; searching for the word would turn a missing argument into a confident
    /// answer about a payee called "unknown".
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("unknown")]
    [InlineData("string")]
    [InlineData("payeeId")]
    public async Task A_placeholder_id_is_not_searched_for(string placeholder)
    {
        var h = Build($"{nameof(A_placeholder_id_is_not_searched_for)}{placeholder}");
        SeedThreeCamilles(h);

        var payload = JsonDocument.Parse(
            await h.Balance.RunAsync($$"""{"payeeId":"{{placeholder}}"}""", default)).RootElement;

        payload.GetProperty("found").GetBoolean().Should().BeFalse();
    }

    // ══ 3e. "THIS IS THE PERSON YOU ASKED FOR" ═══════════════════════════════

    /// <summary>
    /// ★★ THE FAILURE THIS PINS, OBSERVED TWICE IN ONE AFTERNOON AND ONCE BEFORE. The user picked
    /// "Camille Martin · FR-301" off the clarify form; the lookup RESOLVED and returned her balance —
    /// the runtime log says <c>Found</c> — and the assistant answered "I have not found any payee
    /// matching FR-301, please check the identifier". Real money, real person, reported as
    /// non-existent out of a SUCCESSFUL lookup. Rule 19a of the prompt says exactly the right thing
    /// about this and had failed three times by then, so the sentence now travels WITH the data.
    /// </summary>
    [Fact]
    public async Task A_code_match_carries_the_sentence_that_says_it_is_the_same_person()
    {
        var h = Build(nameof(A_code_match_carries_the_sentence_that_says_it_is_the_same_person));
        SeedThreeCamilles(h);

        var payload = JsonDocument.Parse(
            await h.Balance.RunAsync("""{"payeeName":"FR-301"}""", default)).RootElement;

        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("requestedIdentifier").GetString().Should().Be("FR-301");

        var disclosure = payload.GetProperty("disclosure").GetString()!;

        // It has to name BOTH sides of the equivalence — the term the user gave and who it belongs to.
        disclosure.Should().Contain("FR-301").And.Contain("Camille Martin");

        // ★ AND IT HAS TO FORBID THE EXACT SENTENCE THAT WAS WRITTEN, not merely encourage a good one.
        disclosure.Should().Contain("SUCCEEDED");
        disclosure.Should().Contain("must NOT say the payee was not found");
    }

    /// <summary>★ The assignments tool says the same thing, so the two cannot disagree about one code.</summary>
    [Fact]
    public async Task The_assignments_tool_carries_the_same_sentence()
    {
        var h = Build(nameof(The_assignments_tool_carries_the_same_sentence));
        SeedThreeCamilles(h);

        var payload = JsonDocument.Parse(
            await h.Plans.RunAsync("""{"payeeName":"EMP409"}""", default)).RootElement;

        payload.GetProperty("disclosure").GetString()
            .Should().Contain("EMP409").And.Contain("Camille Laurent");
    }

    /// <summary>
    /// ★★ AND AN EXACT NAME SAYS NOTHING. The question and the payload already use the same words, so
    /// there is nothing to reconcile — a reassurance repeated on every turn is noise that teaches the
    /// model to skip the field, which would cost it its effect on the turns that need it.
    /// </summary>
    [Fact]
    public async Task An_exact_name_needs_no_disclosure()
    {
        var h = Build(nameof(An_exact_name_needs_no_disclosure));
        SeedPayee(h, "Camille Martin", "FR-301");

        var payload = JsonDocument.Parse(
            await h.Balance.RunAsync("""{"payeeName":"Camille Martin"}""", default)).RootElement;

        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.TryGetProperty("disclosure", out _).Should().BeFalse();
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

    /// <summary>
    /// ★ ADJACENT TURNS MUST NOT DISAGREE. "Which Anna?" for a balance and "she does not exist" for her
    /// plans, in one conversation, would be worse than the bug being fixed.
    ///
    /// ★★ IT COMPARES THE AMBIGUITY, NOT THE BYTES — and that is a deliberate narrowing of what this
    /// test asserts, not a weakening of it. It used to be `plans.Should().Be(balance)`, which was a
    /// fine shorthand while the two payloads were identical. They are no longer supposed to be: each
    /// now carries a clarify form whose options re-run the tool that produced it, so a user who asked
    /// about assignments and pressed a name gets assignments. That difference is REQUIRED, and it is
    /// pinned by <see cref="The_form_reruns_the_lookup_that_hit_the_ambiguity"/>. What must still be
    /// identical is everything the answer is about: the outcome and the people.
    /// </summary>
    private static async Task AssertSameAmbiguity(Harness h, string name)
    {
        var balance = JsonDocument.Parse(
            await h.Balance.RunAsync($$"""{"payeeName":"{{name}}"}""", default)).RootElement;
        var plans = JsonDocument.Parse(
            await h.Plans.RunAsync($$"""{"payeeName":"{{name}}"}""", default)).RootElement;

        Outcome(plans).Should().Be(PayeeAmbiguity.Outcome);
        Outcome(plans).Should().Be(Outcome(balance));
        plans.GetProperty("found").GetBoolean().Should().Be(balance.GetProperty("found").GetBoolean());
        plans.GetProperty("candidateCount").GetInt32()
            .Should().Be(balance.GetProperty("candidateCount").GetInt32());
        plans.GetProperty("candidates").GetRawText()
            .Should().Be(balance.GetProperty("candidates").GetRawText());
    }

    [Fact]
    public async Task The_assignments_tool_answers_the_same_ambiguity_the_same_way()
    {
        var h = Build(nameof(The_assignments_tool_answers_the_same_ambiguity_the_same_way));
        SeedTwoAnnas(h);

        await AssertSameAmbiguity(h, "Anna Schmidt");
    }
}

internal static class PipeExtensions
{
    /// <summary>Assert on a value inline without naming it. Test-only sugar.</summary>
    public static void Pipe<T>(this T value, Action<T> assert) => assert(value);
}
