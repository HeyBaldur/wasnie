using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Assistant.Tools;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Ledger;
using Wasnie.Application.Compensation.Handlers.Payees;
using Wasnie.Application.Compensation.Queries.Ledger;
using Wasnie.Application.Compensation.Queries.Payees;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The assistant's THIRD tool: a payee's balance, earnings crossed with debt.
///
/// ★ THE GUARDS ARE NOT MOCKED, exactly as in the other two tool suites. The tool is wired to the REAL
/// <c>ListPayeesHandler</c> and <c>GetPayeeLedgerSummaryHandler</c> over a REAL DbContext with a live
/// tenant filter and a permission service that answers from an actual role. Stubbing ISender would prove
/// the tool calls something; what needs proving is that it cannot answer for a payee the caller has no
/// business reading.
/// </summary>
public sealed class AssistantGetPayeeBalanceToolTests
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

        // Same set, asked instead of enforced.
        public Task<bool> HasAsync(string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(_granted.Contains(permission));
    }

    /// <summary>Dispatches to the REAL handlers so no guard can be skipped.</summary>
    private sealed class HandlerSender(
        IApplicationDbContext db,
        IAuthorizationService auth,
        IPayeeAccessGuard guard,
        ITenantContext tenantContext) : ISender
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

                case GetPayeeLedgerSummaryQuery q:
                    return (TResponse)(object)await new GetPayeeLedgerSummaryHandler(
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
        ApplicationDbContext Db, GetPayeeLedgerSummaryTool Tool, HandlerSender Sender, Guid TenantId);

    private static Harness Build(
        string dbName, Guid tenantId, PayeeVisibility? visibility = null, params string[] permissions)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        // ONE database addressed by name, so two harnesses with different tenants see the same rows and
        // only the query filter separates them — otherwise the isolation test passes for lack of data.
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(AssistantGetPayeeBalanceToolTests)}.{dbName}")
                .Options,
            tenantCtx, Substitute.For<IPublisher>());

        var guard = new FakePayeeAccessGuard(visibility ?? PayeeVisibility.Everything);
        var sender = new HandlerSender(db, new RoleAuthorization(permissions), guard, tenantCtx);

        return new Harness(
            db,
            new GetPayeeLedgerSummaryTool(sender, NullLogger<GetPayeeLedgerSummaryTool>.Instance),
            sender,
            tenantId);
    }

    /// <summary>
    /// What the summary actually needs: the right to find a payee, and the right to receive a finished
    /// balance. Payouts.Read is deliberately NOT here — the facade means no caller of this tool ever
    /// needs it, and leaving it in the harness would hide a regression that re-introduced the demand.
    /// </summary>
    private static readonly string[] FinancePermissions =
        [Permission.PayeesRead, Permission.LedgerSummaryRead];

    private static Guid SeedPayee(Harness h, string fullName, string code)
    {
        var payee = Payee.Create(h.TenantId, fullName, code, $"{code}@test.com".ToLowerInvariant(),
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        h.Db.Payees.Add(payee);
        h.Db.SaveChanges();
        return payee.Id;
    }

    private static void SeedPayout(Harness h, Guid payeeId, decimal amount, CompensationPayoutStatus status)
    {
        var spec = new PayoutLineSpec(
            Guid.NewGuid(), Guid.NewGuid(), "Base",
            Money.Of(amount * 10m, Eur), Money.Of(amount, Eur), []);

        var payout = CompensationPayout.Calculate(
            h.TenantId, payeeId, Guid.NewGuid(),
            PayeeReference.Snapshot(payeeId, "Seeded", "EMP-SEED"),
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            [spec], Eur, "test", Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);

        if (status is CompensationPayoutStatus.Approved or CompensationPayoutStatus.Paid)
            payout.Approve("test", Now, Guid.NewGuid());
        if (status == CompensationPayoutStatus.Paid)
            payout.MarkPaid("test", Now);

        h.Db.CompensationPayouts.Add(payout);
        h.Db.SaveChanges();
    }

    private static void SeedDebt(Harness h, Guid payeeId, decimal debt)
    {
        var entry = PayeeLedgerEntry.CreateSystemEntry(
            h.TenantId, payeeId, LedgerTransactionType.ClawbackDebit, Money.Of(debt, Eur),
            "Deal churned.", LedgerSourceType.DealChurn, "system", Guid.NewGuid(), Now, Guid.NewGuid());
        var balance = PayeeBalance.Open(h.TenantId, payeeId, Eur, Guid.NewGuid(), Now);
        balance.Apply(entry, Now);

        h.Db.PayeeLedgerEntries.Add(entry);
        h.Db.PayeeBalances.Add(balance);
        h.Db.SaveChanges();
    }

    private static async Task<JsonElement> RunAsync(Harness h, string payeeName, string? period = null)
    {
        var args = period is null
            ? $$"""{"payeeName":"{{payeeName}}"}"""
            : $$"""{"payeeName":"{{payeeName}}","period":"{{period}}"}""";

        return JsonDocument.Parse(await h.Tool.RunAsync(args, default)).RootElement;
    }

    // ══ THE FALSE ZERO, THROUGH THE TOOL ═════════════════════════════════════

    [Fact]
    public async Task Earnings_with_no_debt_reach_the_model_as_earnings_and_a_token()
    {
        var h = Build(nameof(Earnings_with_no_debt_reach_the_model_as_earnings_and_a_token),
            Guid.NewGuid(), null, FinancePermissions);
        var payeeId = SeedPayee(h, "Ana Sales", "EMP-ANA");
        SeedPayout(h, payeeId, 10_000m, CompensationPayoutStatus.Approved);

        var payload = await RunAsync(h, "Ana Sales");

        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        var eur = payload.GetProperty("balances").EnumerateArray().Single();

        eur.GetProperty("earnedCommissions").GetDecimal().Should().Be(10_000m);
        eur.GetProperty("outstandingDebt").GetDecimal().Should().Be(0m);
        eur.GetProperty("netPendingPayout").GetDecimal().Should().Be(10_000m);
        eur.GetProperty("interpretation").GetString().Should().Be("EarningsAndNoDebt",
            "★ the token is what stops the model reporting the ledger's zero as the balance");
    }

    [Fact]
    public async Task A_debt_reaches_the_model_netted_and_labelled()
    {
        var h = Build(nameof(A_debt_reaches_the_model_netted_and_labelled),
            Guid.NewGuid(), null, FinancePermissions);
        var payeeId = SeedPayee(h, "Bruno Sales", "EMP-BRUNO");
        SeedPayout(h, payeeId, 10_000m, CompensationPayoutStatus.Approved);
        SeedDebt(h, payeeId, 2_500m);

        var eur = (await RunAsync(h, "Bruno Sales")).GetProperty("balances").EnumerateArray().Single();

        eur.GetProperty("outstandingDebt").GetDecimal().Should().Be(2_500m);
        eur.GetProperty("netPendingPayout").GetDecimal().Should().Be(7_500m);
        eur.GetProperty("interpretation").GetString().Should().Be("EarningsWithDebt");
    }

    // ══ RULE 3 — THE REFUSAL SAYS NOTHING ════════════════════════════════════

    [Fact]
    public async Task An_unknown_payee_and_another_tenants_payee_produce_the_same_refusal()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        const string db = nameof(An_unknown_payee_and_another_tenants_payee_produce_the_same_refusal);

        var owner = Build(db, tenantA, null, FinancePermissions);
        var payeeId = SeedPayee(owner, "Carla Sales", "EMP-CARLA");
        SeedPayout(owner, payeeId, 5_000m, CompensationPayoutStatus.Approved);

        // Same database, different tenant: the row is there and the query filter hides it.
        var outsider = Build(db, tenantB, null, FinancePermissions);

        var foreign = await RunAsync(outsider, "Carla Sales");
        var imaginary = await RunAsync(outsider, "Nobody At All");

        foreign.GetRawText().Should().Be(imaginary.GetRawText(),
            "'exists but not yours' and 'does not exist' must be the same sentence");
        foreign.GetProperty("found").GetBoolean().Should().BeFalse();
        foreign.GetProperty("message").GetString().Should().Be(GetPayeeLedgerSummaryTool.RefusalMessage);
    }

    /// <summary>
    /// The resource guard denying a payee the caller can otherwise SEE in the payee list. The refusal
    /// must be the same sentence again — the tool must not leak that the id resolved.
    /// </summary>
    [Fact]
    public async Task A_payee_the_guard_denies_gets_the_same_refusal()
    {
        var h = Build(nameof(A_payee_the_guard_denies_gets_the_same_refusal),
            Guid.NewGuid(), PayeeVisibility.None, FinancePermissions);
        var payeeId = SeedPayee(h, "Dario Sales", "EMP-DARIO");
        SeedPayout(h, payeeId, 9_000m, CompensationPayoutStatus.Approved);

        var payload = await RunAsync(h, "Dario Sales");

        payload.GetProperty("found").GetBoolean().Should().BeFalse();
        payload.GetProperty("message").GetString().Should().Be(GetPayeeLedgerSummaryTool.RefusalMessage);
    }

    /// <summary>
    /// ★ THE FACADE, FROM THE TOOL'S SIDE. LedgerSummary.Read is enough — Payouts.Read is deliberately
    /// absent from this harness, and the earned half still arrives. That is the whole trade: the caller
    /// receives a finished figure without being handed the payroll tables it was computed from.
    /// </summary>
    [Fact]
    public async Task The_summary_permission_alone_produces_the_crossed_answer()
    {
        var h = Build(nameof(The_summary_permission_alone_produces_the_crossed_answer),
            Guid.NewGuid(), null, Permission.PayeesRead, Permission.LedgerSummaryRead);
        var payeeId = SeedPayee(h, "Elena Sales", "EMP-ELENA");
        SeedPayout(h, payeeId, 5_000m, CompensationPayoutStatus.Approved);
        SeedDebt(h, payeeId, 1_200m);

        var eur = (await RunAsync(h, "Elena Sales")).GetProperty("balances").EnumerateArray().Single();

        eur.GetProperty("earnedCommissions").GetDecimal().Should().Be(5_000m);
        eur.GetProperty("netPendingPayout").GetDecimal().Should().Be(3_800m);
    }

    /// <summary>
    /// And without the facade permission there is no answer at all — refused with the shared sentence,
    /// never with the debt half, which would be the false zero arriving through the permission system.
    /// </summary>
    [Fact]
    public async Task Without_the_summary_permission_the_tool_refuses_and_leaks_no_figure()
    {
        var h = Build(nameof(Without_the_summary_permission_the_tool_refuses_and_leaks_no_figure),
            Guid.NewGuid(), null, Permission.PayeesRead, Permission.LedgerRead);
        var payeeId = SeedPayee(h, "Elena Sales", "EMP-ELENA");
        SeedDebt(h, payeeId, 1_200m);

        var payload = await RunAsync(h, "Elena Sales");

        payload.GetProperty("found").GetBoolean().Should().BeFalse();
        payload.GetRawText().Should().NotContain("1200", "the debt must not leak through a refusal");
    }

    // ══ A TECHNICAL FAULT IS LOUD ════════════════════════════════════════════

    /// <summary>
    /// ★ A BROKEN QUERY IS NOT AN EMPTY ONE. Folding a fault into the refusal would tell a user their
    /// colleague does not exist because something crashed. It is raised, so the runner fails the turn
    /// and the user gets a retry card.
    /// </summary>
    [Fact]
    public async Task A_broken_lookup_is_raised_not_reported_as_not_found()
    {
        var h = Build(nameof(A_broken_lookup_is_raised_not_reported_as_not_found),
            Guid.NewGuid(), null, FinancePermissions);
        SeedPayee(h, "Fabio Sales", "EMP-FABIO");
        h.Sender.FailPayeeListQuery = true;

        var act = async () => await RunAsync(h, "Fabio Sales");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ══ NAME RESOLUTION ══════════════════════════════════════════════════════

    [Fact]
    public async Task An_ambiguous_name_is_refused_rather_than_guessed()
    {
        var h = Build(nameof(An_ambiguous_name_is_refused_rather_than_guessed),
            Guid.NewGuid(), null, FinancePermissions);
        SeedPayee(h, "Ana García", "EMP-ANA1");
        SeedPayee(h, "Ana García", "EMP-ANA2");

        var payload = await RunAsync(h, "Ana García");

        payload.GetProperty("found").GetBoolean().Should().BeFalse(
            "putting the wrong colleague's pay on screen is worse than asking again");
    }

    /// <summary>
    /// ★ FOUND IN RUNTIME VERIFICATION, NOT IN REVIEW. Asked about employee code NB-2001, the model
    /// wrote it back with a NON-BREAKING HYPHEN (U+2011) and a real payee with real money came back as
    /// "no encontrado". The identifier passes through a language model, so it must be compared the way
    /// PlanNameMatch already compares plan names — the same bug, through a different tool.
    /// </summary>
    [Theory]
    [InlineData("EMP‑GINA")] // non-breaking hyphen — the observed failure
    [InlineData("EMP–GINA")] // en dash
    [InlineData("  emp-gina  ")]  // padded and re-cased
    public async Task A_code_retyped_with_a_typographic_dash_still_resolves(string typed)
    {
        var h = Build($"{nameof(A_code_retyped_with_a_typographic_dash_still_resolves)}-{typed.Length}",
            Guid.NewGuid(), null, FinancePermissions);
        var payeeId = SeedPayee(h, "Gina Sales", "EMP-GINA");
        SeedPayout(h, payeeId, 2_000m, CompensationPayoutStatus.Approved);

        var payload = await RunAsync(h, typed);

        payload.GetProperty("found").GetBoolean().Should().BeTrue(
            "a typographic substitution must not turn a real payee into 'not found'");
    }

    [Fact]
    public async Task An_employee_code_resolves_the_payee()
    {
        var h = Build(nameof(An_employee_code_resolves_the_payee),
            Guid.NewGuid(), null, FinancePermissions);
        var payeeId = SeedPayee(h, "Gina Sales", "EMP-GINA");
        SeedPayout(h, payeeId, 2_000m, CompensationPayoutStatus.Paid);

        var payload = await RunAsync(h, "EMP-GINA");

        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("payeeName").GetString().Should().Be("Gina Sales");
        // ★ The field that stops the model doubting its own result: the user asked for a CODE, so the
        // name coming back is one they never typed. Without this the model reads the mismatch as "not
        // the payee I asked about" and reports a real person as non-existent — observed in runtime.
        payload.GetProperty("matchedBy").GetString().Should().Be("EmployeeCode");
    }

    [Fact]
    public async Task A_full_name_is_reported_as_an_exact_match()
    {
        var h = Build(nameof(A_full_name_is_reported_as_an_exact_match),
            Guid.NewGuid(), null, FinancePermissions);
        var payeeId = SeedPayee(h, "Iris Sales", "EMP-IRIS");
        SeedPayout(h, payeeId, 1_500m, CompensationPayoutStatus.Approved);

        (await RunAsync(h, "Iris Sales")).GetProperty("matchedBy").GetString().Should().Be("ExactName");
    }

    // ══ GDPR — MINIMAL PII ═══════════════════════════════════════════════════

    /// <summary>
    /// The payload carries the name, the OPAQUE ID, and no other personal data.
    ///
    /// ★ THE ID USED TO BE EXCLUDED HERE, AND THAT WAS THE WRONG CALL. The reasoning was data
    /// minimisation — "an id is of no use in a sentence" — and it was true of the sentence and false of
    /// the CONVERSATION: without the id in the transcript, a follow-up question ("and how much of that
    /// is the clawback?") made the model retype the name from memory, miss, and report a person it had
    /// just described as not found. A GUID is not personal data; it identifies a row, carries no
    /// meaning, and is useless outside a tenant the caller is already authorised for. The email and the
    /// employee code stay out, because those ARE about the person.
    /// </summary>
    [Fact]
    public async Task The_payload_carries_the_id_and_the_name_but_no_personal_data()
    {
        var h = Build(nameof(The_payload_carries_the_id_and_the_name_but_no_personal_data),
            Guid.NewGuid(), null, FinancePermissions);
        var payeeId = SeedPayee(h, "Hugo Sales", "EMP-HUGO");
        SeedPayout(h, payeeId, 1_000m, CompensationPayoutStatus.Approved);

        var payload = await RunAsync(h, "Hugo Sales");
        var raw = payload.GetRawText();

        payload.GetProperty("payeeId").GetGuid().Should().Be(payeeId,
            "the next turn copies this instead of retyping the name");
        raw.Should().Contain("Hugo Sales");
        raw.Should().NotContain("emp-hugo@test.com", "the email is not needed to state a balance");
        raw.Should().NotContain("EMP-HUGO", "the employee code is not needed either");
    }

    // ══ THE ID PATH — recurring references ═══════════════════════════════════

    /// <summary>
    /// ★ THE FIX FOR THE BROKEN SECOND TURN. Turn 1 answers by name and hands back an id; turn 2 sends
    /// the id alone and gets the same payee, with no name resolution to get wrong.
    /// </summary>
    [Fact]
    public async Task An_id_alone_answers_without_any_name_resolution()
    {
        var h = Build(nameof(An_id_alone_answers_without_any_name_resolution),
            Guid.NewGuid(), null, FinancePermissions);
        var payeeId = SeedPayee(h, "Julia Sales", "EMP-JULIA");
        SeedPayout(h, payeeId, 8_000m, CompensationPayoutStatus.Approved);

        var payload = JsonDocument.Parse(
            await h.Tool.RunAsync($$"""{"payeeId":"{{payeeId}}"}""", default)).RootElement;

        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("payeeName").GetString().Should().Be("Julia Sales");
        payload.GetProperty("matchedBy").GetString().Should().Be("ResolvedById");
        payload.GetProperty("balances").EnumerateArray().Single()
            .GetProperty("netPendingPayout").GetDecimal().Should().Be(8_000m);
    }

    /// <summary>
    /// ★★ THE ID BUYS PRECISION, NEVER ACCESS. An id the caller may not see — lifted from anywhere —
    /// takes the SAME indistinguishable refusal as a payee that does not exist. The guard runs on
    /// whatever arrives; skipping name resolution skips nothing else.
    /// </summary>
    [Fact]
    public async Task An_id_the_guard_denies_is_refused_exactly_like_an_unknown_one()
    {
        var h = Build(nameof(An_id_the_guard_denies_is_refused_exactly_like_an_unknown_one),
            Guid.NewGuid(), PayeeVisibility.None, FinancePermissions);
        var payeeId = SeedPayee(h, "Karim Sales", "EMP-KARIM");
        SeedPayout(h, payeeId, 40_000m, CompensationPayoutStatus.Approved);

        var denied = JsonDocument.Parse(
            await h.Tool.RunAsync($$"""{"payeeId":"{{payeeId}}"}""", default)).RootElement;
        var imaginary = JsonDocument.Parse(
            await h.Tool.RunAsync($$"""{"payeeId":"{{Guid.NewGuid()}}"}""", default)).RootElement;

        denied.GetRawText().Should().Be(imaginary.GetRawText());
        denied.GetRawText().Should().NotContain("40000");
    }

    /// <summary>
    /// A malformed id must not cost the user their answer: the name it arrived with is a perfectly good
    /// second chance, and models do occasionally write a placeholder into an argument.
    /// </summary>
    [Fact]
    public async Task An_unparseable_id_falls_back_to_the_name()
    {
        var h = Build(nameof(An_unparseable_id_falls_back_to_the_name),
            Guid.NewGuid(), null, FinancePermissions);
        var payeeId = SeedPayee(h, "Lena Sales", "EMP-LENA");
        SeedPayout(h, payeeId, 900m, CompensationPayoutStatus.Approved);

        var payload = JsonDocument.Parse(await h.Tool.RunAsync(
            """{"payeeId":"not-a-guid","payeeName":"Lena Sales"}""", default)).RootElement;

        payload.GetProperty("found").GetBoolean().Should().BeTrue();
        payload.GetProperty("matchedBy").GetString().Should().Be("ExactName");
    }
}
