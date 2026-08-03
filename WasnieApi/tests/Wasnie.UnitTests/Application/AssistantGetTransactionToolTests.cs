using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Tools;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Handlers.Credits;
using Wasnie.Application.Compensation.Handlers.Transactions;
using Wasnie.Application.Compensation.Queries.Credits;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The assistant's ONE read-only tool, and the three rules it exists under.
///
/// ★ THESE TESTS DO NOT MOCK THE GUARDS — that is the entire point of them. The tool is wired to the
/// REAL <c>ListTransactionsHandler</c> and <c>ListCreditsHandler</c>, over a REAL
/// <c>ApplicationDbContext</c> whose tenant query filter is live, behind a permission service that
/// answers from an actual role. A test that stubbed <c>ISender</c> would prove the tool calls something,
/// which is not the property anyone is worried about. The property is that a user cannot read a record
/// they have no business reading — and that can only be shown by letting the machinery that stops them
/// actually run.
/// </summary>
public sealed class AssistantGetTransactionToolTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Permission checks against a role, with the same shape as the real service: return quietly when
    /// the role has the permission, throw <see cref="ForbiddenException"/> when it does not.
    /// </summary>
    private sealed class RoleAuthorization(params string[] permissions) : IAuthorizationService
    {
        private readonly HashSet<string> _granted = new(permissions, StringComparer.OrdinalIgnoreCase);

        public Task RequireAsync(string permission, CancellationToken cancellationToken = default) =>
            _granted.Contains(permission)
                ? Task.CompletedTask
                : throw new ForbiddenException(permission);
    }

    /// <summary>
    /// Dispatches to the REAL query handlers. The tool must not be able to tell this from MediatR, and
    /// MediatR must not be able to smuggle in a handler that skips a guard — so the wiring is explicit.
    /// </summary>
    private sealed class HandlerSender(IApplicationDbContext db, IAuthorizationService auth) : ISender
    {
        public int TransactionQueries { get; private set; }

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            switch (request)
            {
                case ListTransactionsQuery q:
                    TransactionQueries++;
                    return (TResponse)(object)await new ListTransactionsHandler(db, auth)
                        .Handle(q, cancellationToken);

                case ListCreditsQuery q:
                    return (TResponse)(object)await new ListCreditsHandler(db, auth)
                        .Handle(q, cancellationToken);

                default:
                    // Payout lookups are not reached by these tests (no payouts are seeded); anything
                    // else arriving here is a change nobody meant to make.
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
        ApplicationDbContext Db, GetTransactionTool Tool, HandlerSender Sender, Guid TenantId);

    /// <param name="permissions">The asking user's role, as a set of permissions.</param>
    private static Harness Build(string dbName, Guid tenantId, params string[] permissions)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        // ★ ONE database, addressed by name, so two harnesses with DIFFERENT tenants see the SAME rows
        // and only the query filter separates them. A per-test database would make isolation pass for
        // the wrong reason — there would be nothing to leak.
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(AssistantGetTransactionToolTests)}.{dbName}")
                .Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var sender = new HandlerSender(db, new RoleAuthorization(permissions));

        return new Harness(
            db, new GetTransactionTool(sender, NullLogger<GetTransactionTool>.Instance), sender, tenantId);
    }

    private static CompensationTransaction Seed(
        ApplicationDbContext db, Guid tenantId, string reference, decimal amount = 1200m)
    {
        var tx = CompensationTransaction.Ingest(
            tenantId: tenantId,
            referenceNumber: reference,
            payeeId: null,
            amount: Money.Of(amount, "EUR"),
            transactionDate: new DateOnly(2026, 5, 14),
            source: TransactionSource.Manual,
            ingestedBy: "seed",
            id: Guid.NewGuid(),
            now: Now,
            eventId: Guid.NewGuid(),
            description: "Acme renewal");

        db.CompensationTransactions.Add(tx);
        db.SaveChanges();
        return tx;
    }

    private static async Task<JsonElement> RunAsync(Harness h, string reference)
    {
        var json = await h.Tool.RunAsync($$"""{"reference":"{{reference}}"}""", CancellationToken.None);
        return JsonDocument.Parse(json).RootElement;
    }

    // ── Test 1 — the lookup goes through the domain query ─────────────────────

    [Fact]
    public async Task The_tool_reads_the_transaction_THROUGH_the_domain_query()
    {
        // ★ RULE 1. The reference is resolved by sending ListTransactionsQuery — the same query the
        // transactions screen sends — not by touching the DbContext. `TransactionQueries` counts the
        // dispatches, so a future "optimisation" that reads the table directly stops being invisible.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(The_tool_reads_the_transaction_THROUGH_the_domain_query), tenant,
            Permission.TransactionsRead, Permission.CreditsRead);
        Seed(h.Db, tenant, "TERM-CC-10");

        var result = await RunAsync(h, "TERM-CC-10");

        h.Sender.TransactionQueries.Should().Be(1, "the domain query IS the access path");
        result.GetProperty("found").GetBoolean().Should().BeTrue();
        result.GetProperty("reference").GetString().Should().Be("TERM-CC-10");
    }

    [Fact]
    public async Task An_exact_reference_is_required_so_a_prefix_cannot_answer_for_another_sale()
    {
        // The domain's `Reference` filter is a SUBSTRING search: asking it for "TERM-CC-1" would happily
        // return "TERM-CC-10". Answering a question about one sale with another sale's money is the
        // quiet kind of wrong this product cannot afford, so the tool matches exactly.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(An_exact_reference_is_required_so_a_prefix_cannot_answer_for_another_sale), tenant,
            Permission.TransactionsRead, Permission.CreditsRead);
        Seed(h.Db, tenant, "TERM-CC-10");

        var result = await RunAsync(h, "TERM-CC-1");

        result.GetProperty("found").GetBoolean().Should().BeFalse();
    }

    // ── Test 2 — ★ ISOLATION ──────────────────────────────────────────────────

    [Fact]
    public async Task A_user_CANNOT_read_a_transaction_belonging_to_another_tenant()
    {
        // ★ THE TEST THAT MATTERS MOST. Alice's tenant owns TERM-CC-10; Bob's tenant asks for it over
        // the SAME database. The tenant query filter — not a check written in the tool — is what makes
        // it invisible. Remove `HasQueryFilter` for CompensationTransaction and this goes red.
        var db = nameof(A_user_CANNOT_read_a_transaction_belonging_to_another_tenant);
        var aliceTenant = Guid.NewGuid();
        var bobTenant = Guid.NewGuid();

        var alice = Build(db, aliceTenant, Permission.TransactionsRead, Permission.CreditsRead);
        Seed(alice.Db, aliceTenant, "TERM-CC-10");

        // Alice sees her own sale...
        (await RunAsync(alice, "TERM-CC-10")).GetProperty("found").GetBoolean().Should().BeTrue();

        // ...and Bob, over the same store, does not.
        var bob = Build(db, bobTenant, Permission.TransactionsRead, Permission.CreditsRead);
        var bobResult = await RunAsync(bob, "TERM-CC-10");

        bobResult.GetProperty("found").GetBoolean().Should().BeFalse();
        // Not one field of the record leaked into the refusal.
        bobResult.TryGetProperty("saleAmount", out _).Should().BeFalse();
        bobResult.TryGetProperty("payeeName", out _).Should().BeFalse();
        bobResult.GetRawText().Should().NotContain("Acme renewal");
    }

    [Fact]
    public async Task A_user_whose_ROLE_cannot_read_transactions_gets_nothing()
    {
        // The other half of isolation: same tenant, wrong role. Carol's role has no Transactions.Read,
        // so the handler's own guard throws before a row is touched. Remove the RequireAsync line from
        // ListTransactionsHandler and this goes red.
        var db = nameof(A_user_whose_ROLE_cannot_read_transactions_gets_nothing);
        var tenant = Guid.NewGuid();

        var alice = Build(db, tenant, Permission.TransactionsRead, Permission.CreditsRead);
        Seed(alice.Db, tenant, "TERM-CC-10");

        var carol = Build(db, tenant, Permission.CreditsRead); // no Transactions.Read
        var result = await RunAsync(carol, "TERM-CC-10");

        result.GetProperty("found").GetBoolean().Should().BeFalse();
    }

    // ── Test 3 — ★ INDISTINGUISHABLE REFUSAL ──────────────────────────────────

    [Fact]
    public async Task Not_found_and_not_allowed_are_BYTE_IDENTICAL()
    {
        // ★ RULE 3. Three different reasons to say no — the reference does not exist, it belongs to
        // another tenant, the role may not read transactions — and ONE answer, compared as raw JSON
        // rather than field by field so a helpful extra hint cannot slip in beside the message.
        //
        // A different reply for "exists but not yours" would confirm the reference is real, which is the
        // fact somebody probing is fishing for. The useful answer and the leak are the same sentence.
        var db = nameof(Not_found_and_not_allowed_are_BYTE_IDENTICAL);
        var aliceTenant = Guid.NewGuid();

        var alice = Build(db, aliceTenant, Permission.TransactionsRead, Permission.CreditsRead);
        Seed(alice.Db, aliceTenant, "TERM-CC-10");

        var doesNotExist = await alice.Tool.RunAsync(
            """{"reference":"NO-SUCH-REFERENCE"}""", CancellationToken.None);

        var otherTenant = Build(db, Guid.NewGuid(), Permission.TransactionsRead, Permission.CreditsRead);
        var notMine = await otherTenant.Tool.RunAsync(
            """{"reference":"TERM-CC-10"}""", CancellationToken.None);

        var noPermission = Build(db, aliceTenant, Permission.CreditsRead);
        var forbidden = await noPermission.Tool.RunAsync(
            """{"reference":"TERM-CC-10"}""", CancellationToken.None);

        notMine.Should().Be(doesNotExist, "a real reference must not be distinguishable from a fake one");
        forbidden.Should().Be(doesNotExist, "a forbidden reference must not be distinguishable either");

        // And the sentence itself names neither cause.
        doesNotExist.Should().Contain(GetTransactionTool.RefusalMessage);
        doesNotExist.Should().NotContain("permission");
        doesNotExist.Should().NotContain("tenant");
    }

    [Fact]
    public async Task Unreadable_arguments_refuse_the_same_way_as_everything_else()
    {
        var tenant = Guid.NewGuid();
        var h = Build(nameof(Unreadable_arguments_refuse_the_same_way_as_everything_else), tenant,
            Permission.TransactionsRead, Permission.CreditsRead);

        var garbage = await h.Tool.RunAsync("not json at all", CancellationToken.None);
        var empty = await h.Tool.RunAsync("""{"reference":"   "}""", CancellationToken.None);

        garbage.Should().Contain(GetTransactionTool.RefusalMessage);
        empty.Should().Be(garbage);
    }

    // ── Test 4 — the JSON is named for what it MEANS ──────────────────────────

    [Fact]
    public async Task The_payload_uses_names_a_model_cannot_misread()
    {
        // ★ `amount` beside `total` forces the model to guess which is the sale and which is the
        // commission, and its guess becomes a number in front of a user who believes it. These names
        // remove the guess.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(The_payload_uses_names_a_model_cannot_misread), tenant,
            Permission.TransactionsRead, Permission.CreditsRead);
        Seed(h.Db, tenant, "TERM-CC-10", amount: 1200m);

        var result = await RunAsync(h, "TERM-CC-10");

        result.GetProperty("saleAmount").GetDecimal().Should().Be(1200m);
        result.GetProperty("saleCurrency").GetString().Should().Be("EUR");
        result.GetProperty("transactionDate").GetString().Should().Be("2026-05-14");
        result.GetProperty("transactionStatus").GetString().Should().NotBeNullOrWhiteSpace();
        result.GetProperty("commissionGenerated").GetBoolean().Should().BeFalse("nothing credited it yet");
        result.GetProperty("hasBeenPaid").GetBoolean().Should().BeFalse();
        result.GetProperty("settlementNote").GetString().Should().Contain("not produced any commission");

        // A bare `amount` or `status` would be exactly the ambiguity this is avoiding.
        result.TryGetProperty("amount", out _).Should().BeFalse();
        result.TryGetProperty("total", out _).Should().BeFalse();
    }

    [Fact]
    public void The_schema_tells_the_model_what_the_argument_IS()
    {
        var tool = new GetTransactionTool(
            Substitute.For<ISender>(), NullLogger<GetTransactionTool>.Instance);

        tool.Schema.Name.Should().Be("get_transaction");
        tool.Schema.Description.Should().Contain("Read-only");

        var parameters = JsonDocument.Parse(tool.Schema.ParametersJson).RootElement;
        parameters.GetProperty("properties").GetProperty("reference")
            .GetProperty("description").GetString().Should().Contain("TERM-CC-10",
                "an example is what stops the model inventing an id format");
        parameters.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("reference");
    }

    // ── Test 5 — READ-ONLY, structurally ──────────────────────────────────────

    [Fact]
    public async Task The_tool_writes_NOTHING()
    {
        // ★ Asserted by watching the database, not by reading the code. The tool answers a question
        // about a record; if a future edit ever made it touch one, the row count or the row itself
        // would move and this would say so.
        var tenant = Guid.NewGuid();
        var h = Build(nameof(The_tool_writes_NOTHING), tenant,
            Permission.TransactionsRead, Permission.CreditsRead);
        var seeded = Seed(h.Db, tenant, "TERM-CC-10");
        var before = seeded.UpdatedAt;

        await RunAsync(h, "TERM-CC-10");
        await RunAsync(h, "NO-SUCH-REFERENCE");

        var rows = await h.Db.CompensationTransactions.IgnoreQueryFilters().ToListAsync();
        rows.Should().HaveCount(1, "a lookup must not create anything");
        rows[0].UpdatedAt.Should().Be(before, "a lookup must not modify anything");
        rows[0].Status.Should().Be(seeded.Status);
    }

    [Fact]
    public void The_tool_CONTRACT_has_no_way_to_write()
    {
        // ★ STRUCTURAL, not behavioural. The danger is not this tool — it is the next one, added by
        // someone who sees `IAssistantTool` and finds a method that would let it act. There is exactly
        // one method, it is called RunAsync, and it returns a string. Anything named to suggest a
        // mutation appearing on this interface should fail here first.
        var methods = typeof(IAssistantTool).GetMethods()
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToList();

        methods.Should().BeEquivalentTo(["RunAsync"]);

        foreach (var forbidden in new[] { "Write", "Execute", "Apply", "Create", "Update", "Delete", "Save" })
        {
            typeof(IAssistantTool).GetMethods().Should().NotContain(
                m => m.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"a tool must never be able to {forbidden.ToLowerInvariant()} anything");
        }
    }

    [Fact]
    public void The_tool_holds_NO_database_context_of_its_own()
    {
        // ★ RULE 1, made structural. A DbContext on this class would be the shortcut past every guard
        // the other tests rely on, and it would look like a harmless performance change in review.
        var dependencies = typeof(GetTransactionTool)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType.Name)
            .ToList();

        dependencies.Should().NotContain(n => n.Contains("DbContext", StringComparison.OrdinalIgnoreCase));
        dependencies.Should().Contain("ISender", "the domain queries ARE the access path");
    }
}
