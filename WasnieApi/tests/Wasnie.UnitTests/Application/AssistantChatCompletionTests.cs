using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Assistant.Commands;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Assistant.Handlers;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Assistant;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The assistant answering through a real chat model — with a FAKE provider.
///
/// ★ NO TEST HERE TOUCHES GROQ. The provider is an interface precisely so the chat can be tested
/// without a network, a key or a bill; a test that called the real service would be slow, flaky, and
/// would fail on someone else's machine for reasons that have nothing to do with the code.
/// </summary>
public sealed class AssistantChatCompletionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
    private const string AliceId = "user-alice";
    private const string BobId = "user-bob";

    /// <summary>A provider that yields the fragments it was given, or throws before the first one.</summary>
    private sealed class FakeProvider : IChatCompletionProvider
    {
        private readonly string[] _fragments;
        private readonly ChatCompletionException? _failure;
        private readonly int _failAfter;

        public FakeProvider(
            string[]? fragments = null,
            ChatCompletionException? failure = null,
            int failAfter = 0,
            bool configured = true)
        {
            _fragments = fragments ?? [];
            _failure = failure;
            _failAfter = failAfter;
            IsConfigured = configured;
        }

        public bool IsConfigured { get; }

        /// <summary>Routing is exercised in AssistantRoutingTests; here it always says "section 1".</summary>
        public Task<string> CompleteJsonAsync(
            IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken) =>
            Task.FromResult("""{"sections":["1"]}""");

        /// <summary>What the tool dispatcher should answer. Null (the default) means "no lookup".</summary>
        public AssistantToolRequest? ToolChoice { get; set; }

        public IReadOnlyList<AssistantToolSchema>? ToolsOffered { get; private set; }

        /// <summary>Set to make the tool-selection call fail, as the provider's 400/429 would.</summary>
        public ChatCompletionException? ToolFailure { get; set; }

        public Task<AssistantToolRequest?> SelectToolAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<AssistantToolSchema> tools,
            CancellationToken cancellationToken)
        {
            ToolsOffered = tools;

            if (ToolFailure is not null)
            {
                throw ToolFailure;
            }

            return Task.FromResult(ToolChoice);
        }

        /// <summary>What the handler actually asked the model — the assertion surface for the prompt.</summary>
        public IReadOnlyList<ChatMessage>? Received { get; private set; }

        public async IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Received = messages;

            for (var i = 0; i < _fragments.Length; i++)
            {
                if (_failure is not null && i == _failAfter)
                {
                    throw _failure;
                }

                await Task.Yield();
                yield return _fragments[i];
            }

            if (_failure is not null && _failAfter >= _fragments.Length)
            {
                throw _failure;
            }
        }
    }

    /// <summary>
    /// A small stand-in corpus. The REAL guide is exercised in AssistantConfinementTests; here the
    /// point is the exchange mechanics, and fifteen thousand tokens of prose in every assertion
    /// message would only make failures unreadable.
    /// </summary>
    private const string TestDoc = "Wasnie test documentation.";

    private static IAssistantKnowledgeBase Knowledge()
    {
        var knowledge = Substitute.For<IAssistantKnowledgeBase>();
        knowledge.Documentation.Returns(TestDoc);
        knowledge.IsAvailable.Returns(true);
        knowledge.Sections.Returns([new DocumentationSection("1", "Only section", TestDoc)]);
        knowledge.TableOfContents.Returns("1: Only section");
        knowledge.TextFor(Arg.Any<IEnumerable<string>>()).Returns(TestDoc);
        return knowledge;
    }

    private const string TestMap = "/plans | Plans | The list of plans.";

    /// <summary>A stand-in map: these tests are about the exchange, not about which routes exist.</summary>
    private static IUiNavigationMap NavigationMap()
    {
        var navigation = Substitute.For<IUiNavigationMap>();
        navigation.PromptBlock.Returns(TestMap);
        navigation.IsAvailable.Returns(true);
        navigation.Routes.Returns(["/plans"]);
        return navigation;
    }

    private sealed record Harness(
        ApplicationDbContext Db,
        StreamAssistantReplyHandler Handler,
        FakeProvider Provider,
        Guid TenantId,
        ICurrentUserService User,
        ITenantContext Tenant);

    private static Harness Build(
        string dbName, FakeProvider provider, string userId = AliceId, Guid? tenantId = null,
        IEnumerable<IAssistantTool>? tools = null)
    {
        var tenant = tenantId ?? Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenant);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{nameof(AssistantChatCompletionTests)}.{dbName}")
                .Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(userId);

        var entitlement = Substitute.For<IAssistantEntitlement>();
        entitlement.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        entitlement.RequireAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var knowledge = Knowledge();

        var handler = new StreamAssistantReplyHandler(
            db, tenantCtx, user, new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            entitlement, provider, knowledge, NavigationMap(),
            new AssistantSectionRouter(provider, knowledge, NullLogger<AssistantSectionRouter>.Instance),
            new AssistantToolRunner(provider, tools ?? [], NullLogger<AssistantToolRunner>.Instance),
            Options.Create(new GroqOptions { ApiKey = "test-key" }));

        return new Harness(db, handler, provider, tenant, user, tenantCtx);
    }

    private static AssistantConversation SeedConversation(Harness h, string ownerId = AliceId)
    {
        var conversation = AssistantConversation.Start(
            Guid.NewGuid(), h.TenantId, ownerId, "Chat", Now);
        h.Db.AssistantConversations.Add(conversation);
        h.Db.SaveChanges();
        return conversation;
    }

    private static async Task<List<AssistantStreamEvent>> DrainAsync(
        StreamAssistantReplyHandler handler, Guid conversationId, string content)
    {
        var frames = new List<AssistantStreamEvent>();
        await foreach (var frame in handler.Handle(
            new StreamAssistantReplyCommand(conversationId, content), CancellationToken.None))
        {
            frames.Add(frame);
        }
        return frames;
    }

    // ── 1. The model is asked, and its answer is stored ───────────────────────

    [Fact]
    public async Task The_handler_sends_the_history_plus_the_new_turn_and_persists_the_answer()
    {
        var provider = new FakeProvider(["Accelerators ", "pay ", "above quota."]);
        var h = Build(nameof(The_handler_sends_the_history_plus_the_new_turn_and_persists_the_answer), provider);
        var conversation = SeedConversation(h);

        var frames = await DrainAsync(h.Handler, conversation.Id, "How do accelerators work?");

        // What the model received: the system prompt, then the conversation.
        provider.Received.Should().NotBeNull();
        provider.Received![0].Role.Should().Be(ChatMessage.SystemRole);
        provider.Received[0].Content.Should().Be(
            AssistantPrompt.BuildSystemMessage(TestDoc, documentationAvailable: true, navigationMap: TestMap));
        // ★ 2b: the documentation really is in what the model receives.
        provider.Received[0].Content.Should().Contain(TestDoc);
        // ★ Navigable guidance: so does the navigation map, on the SAME call — step 2, never step 1.
        provider.Received[0].Content.Should().Contain(TestMap);
        provider.Received[^1].Role.Should().Be(ChatMessage.UserRole);
        provider.Received[^1].Content.Should().Be("How do accelerators work?");

        // What the client saw: the stored user turn, the fragments in order, then the stored answer.
        frames.Select(f => f.Type).Should().Equal(
            AssistantStreamEvent.UserTurn,
            AssistantStreamEvent.Fragment,
            AssistantStreamEvent.Fragment,
            AssistantStreamEvent.Fragment,
            AssistantStreamEvent.Done);

        frames.Where(f => f.Type == AssistantStreamEvent.Fragment).Select(f => f.Delta)
            .Should().Equal("Accelerators ", "pay ", "above quota.");

        // What the database holds: the assembled answer, as an assistant turn.
        var stored = await h.Db.AssistantMessages.IgnoreQueryFilters().OrderBy(m => m.Sequence).ToListAsync();
        stored.Should().HaveCount(2);
        stored[0].Role.Should().Be(AssistantMessageRole.User);
        stored[1].Role.Should().Be(AssistantMessageRole.Assistant);
        stored[1].Content.Should().Be("Accelerators pay above quota.");
        stored[1].TenantId.Should().Be(h.TenantId);
    }

    [Fact]
    public async Task The_conversation_is_named_after_the_first_message_end_to_end()
    {
        // The whole path, not just the helper: a thread starts untitled and comes out of the first
        // exchange wearing the user's own words.
        var provider = new FakeProvider(["ok"]);
        var h = Build(nameof(The_conversation_is_named_after_the_first_message_end_to_end), provider);

        var conversation = AssistantConversation.Start(
            Guid.NewGuid(), h.TenantId, AliceId, AssistantConversation.UntitledSentinel, Now);
        h.Db.AssistantConversations.Add(conversation);
        await h.Db.SaveChangesAsync();

        await DrainAsync(h.Handler, conversation.Id, "¿Cómo creo un plan de comisiones?");

        var stored = await h.Db.AssistantConversations.IgnoreQueryFilters().SingleAsync();
        stored.Title.Should().Be("¿Cómo creo un plan de comisiones?");
        stored.IsUntitled.Should().BeFalse();

        // ★ And the SECOND message leaves the name alone.
        await DrainAsync(h.Handler, conversation.Id, "otra pregunta totalmente distinta");

        var after = await h.Db.AssistantConversations.IgnoreQueryFilters().SingleAsync();
        after.Title.Should().Be("¿Cómo creo un plan de comisiones?");
    }

    [Fact]
    public async Task Prior_turns_are_replayed_to_the_model_in_order()
    {
        var provider = new FakeProvider(["ok"]);
        var h = Build(nameof(Prior_turns_are_replayed_to_the_model_in_order), provider);
        var conversation = SeedConversation(h);

        h.Db.AssistantMessages.AddRange(
            AssistantMessage.Create(Guid.NewGuid(), conversation.Id, h.TenantId, AssistantMessageRole.User, "first", 0, Now),
            AssistantMessage.Create(Guid.NewGuid(), conversation.Id, h.TenantId, AssistantMessageRole.Assistant, "answer", 1, Now));
        await h.Db.SaveChangesAsync();

        await DrainAsync(h.Handler, conversation.Id, "second");

        provider.Received!.Select(m => m.Content).Should().Equal(
            AssistantPrompt.BuildSystemMessage(TestDoc, documentationAvailable: true, navigationMap: TestMap),
            "first", "answer", "second");
        provider.Received.Select(m => m.Role).Should().Equal(
            ChatMessage.SystemRole, ChatMessage.UserRole, ChatMessage.AssistantRole, ChatMessage.UserRole);
    }

    // ── 1b. ★ Tool calling: the model asks, the backend runs it, the answer carries the data ──

    /// <summary>A tool that records what it was asked and returns a fixed payload.</summary>
    private sealed class SpyTool : IAssistantTool
    {
        public AssistantToolSchema Schema { get; } = new(
            "get_transaction", "Look up one sales transaction. Read-only.",
            """{"type":"object","properties":{"reference":{"type":"string"}},"required":["reference"]}""");

        public string? ArgumentsReceived { get; private set; }

        public Task<string> RunAsync(string argumentsJson, CancellationToken cancellationToken)
        {
            ArgumentsReceived = argumentsJson;
            return Task.FromResult("""{"found":true,"reference":"TERM-CC-10","saleAmount":1200.00}""");
        }
    }

    [Fact]
    public async Task Asked_about_a_transaction_the_backend_RUNS_the_tool_and_hands_the_result_to_the_model()
    {
        // ★ THE WHOLE FLOW, end to end: the model asks for `get_transaction`, the BACKEND intercepts it
        // and runs the lookup (the model never touches data), and what comes back is in the context the
        // generating call reads. Stop injecting the tool result into the prompt and this goes red — the
        // assistant would answer "I do not have that information" with the answer in its hand.
        var provider = new FakeProvider(["It was settled in May."])
        {
            ToolChoice = new AssistantToolRequest("get_transaction", """{"reference":"TERM-CC-10"}"""),
        };
        var tool = new SpyTool();
        var h = Build(
            nameof(Asked_about_a_transaction_the_backend_RUNS_the_tool_and_hands_the_result_to_the_model),
            provider, tools: [tool]);
        var conversation = SeedConversation(h);

        await DrainAsync(h.Handler, conversation.Id, "What happened with TERM-CC-10?");

        // The tool was offered, chosen, and run with the reference the user wrote.
        provider.ToolsOffered.Should().ContainSingle().Which.Name.Should().Be("get_transaction");
        tool.ArgumentsReceived.Should().Contain("TERM-CC-10");

        // ...and its result reached the generating call, delimited as DATA rather than as documentation.
        var system = provider.Received!.Single(m => m.Role == ChatMessage.SystemRole).Content;
        system.Should().Contain(AssistantPrompt.DataHeader);
        system.Should().Contain("\"saleAmount\":1200.00");
        system.Should().Contain(AssistantPrompt.DataFooter);
        // The rule that stops the model dressing up a refusal travels with the data.
        system.Should().Contain("found is false");
    }

    [Fact]
    public async Task A_question_needing_no_lookup_runs_NO_tool()
    {
        // Most questions are about how the product works. Calling a lookup "just in case" spends a
        // database round trip to answer nothing, and puts an empty record in front of a model that then
        // has to explain it.
        var provider = new FakeProvider(["A plan is..."]) { ToolChoice = null };
        var tool = new SpyTool();
        var h = Build(nameof(A_question_needing_no_lookup_runs_NO_tool), provider, tools: [tool]);
        var conversation = SeedConversation(h);

        await DrainAsync(h.Handler, conversation.Id, "What is a plan?");

        tool.ArgumentsReceived.Should().BeNull();
        provider.Received!.Single(m => m.Role == ChatMessage.SystemRole).Content
            .Should().NotContain(AssistantPrompt.DataHeader);
    }

    [Fact]
    public async Task A_tool_the_model_INVENTED_is_ignored_rather_than_matched_to_a_real_one()
    {
        // A model naming a tool that does not exist is a hallucination like any other. It must not be
        // resolved to the nearest real tool — running the wrong lookup is worse than running none.
        var provider = new FakeProvider(["..."])
        {
            ToolChoice = new AssistantToolRequest("get_payee_balance", """{"payee":"anyone"}"""),
        };
        var tool = new SpyTool();
        var h = Build(nameof(A_tool_the_model_INVENTED_is_ignored_rather_than_matched_to_a_real_one),
            provider, tools: [tool]);
        var conversation = SeedConversation(h);

        await DrainAsync(h.Handler, conversation.Id, "What is Bob owed?");

        tool.ArgumentsReceived.Should().BeNull();
    }

    [Fact]
    public async Task A_tool_that_THROWS_costs_the_data_not_the_answer()
    {
        // An optional enrichment failing must not turn a question into an error. The assistant answers
        // from the documentation, exactly as it did before tools existed.
        var provider = new FakeProvider(["Here is what the guide says."])
        {
            ToolChoice = new AssistantToolRequest("get_transaction", """{"reference":"X"}"""),
        };
        var h = Build(nameof(A_tool_that_THROWS_costs_the_data_not_the_answer), provider,
            tools: [new ThrowingTool()]);
        var conversation = SeedConversation(h);

        var frames = await DrainAsync(h.Handler, conversation.Id, "What happened with X?");

        frames.Should().NotContain(f => f.Type == "error");
        provider.Received!.Single(m => m.Role == ChatMessage.SystemRole).Content
            .Should().NotContain(AssistantPrompt.DataHeader);
    }

    private sealed class ThrowingTool : IAssistantTool
    {
        public AssistantToolSchema Schema { get; } = new(
            "get_transaction", "Look up one sales transaction. Read-only.", """{"type":"object"}""");

        public Task<string> RunAsync(string argumentsJson, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the database is down");
    }

    // ── 1c. The 400 and the 429 ───────────────────────────────────────────────

    [Fact]
    public async Task A_tool_call_the_PROVIDER_rejected_costs_the_lookup_not_the_turn()
    {
        // ★ THE 400 FROM THE LOG. `tool_use_failed` is the provider refusing the model's OWN generated
        // call. It is a generation fumble — the identical request succeeds on the next attempt — so the
        // turn must survive it: the assistant answers from the documentation, without live data, and
        // the user sees an answer rather than an error. Remove the catch and this goes red.
        var provider = new FakeProvider(["Here is what the guide says."])
        {
            ToolFailure = new ChatCompletionException(
                ChatCompletionException.ToolCallRejected, "tool_use_failed"),
        };
        var h = Build(nameof(A_tool_call_the_PROVIDER_rejected_costs_the_lookup_not_the_turn),
            provider, tools: [new SpyTool()]);
        var conversation = SeedConversation(h);

        var frames = await DrainAsync(h.Handler, conversation.Id, "What happened with TERM-CC-10?");

        frames.Should().NotContain(f => f.Type == "error", "a rejected tool call is recoverable");
        frames.Should().Contain(f => f.Type == "done");
        provider.Received!.Single(m => m.Role == ChatMessage.SystemRole).Content
            .Should().NotContain(AssistantPrompt.DataHeader);
    }

    [Fact]
    public async Task A_rate_limited_answer_reaches_the_user_as_ITS_OWN_key_with_the_question_kept()
    {
        // ★ 429 IS NOT A 500. It arrives as the rate-limit translation key — which the client renders as
        // "the assistant is busy, your message was saved" — and NEVER as the provider's own sentence.
        // And the question really IS saved: that is what makes Retry an offer rather than a lie.
        var provider = new FakeProvider(
            failure: new ChatCompletionException(ChatCompletionException.RateLimited, "429"));
        var h = Build(nameof(A_rate_limited_answer_reaches_the_user_as_ITS_OWN_key_with_the_question_kept),
            provider);
        var conversation = SeedConversation(h);

        var frames = await DrainAsync(h.Handler, conversation.Id, "What happened with TERM-CC-10?");

        var error = frames.Single(f => f.Type == "error");
        error.ErrorKey.Should().Be(ChatCompletionException.RateLimited);
        error.ErrorKey.Should().StartWith("ASSISTANT.", "the client translates a KEY, never vendor text");

        // The user's turn is stored; no assistant row was written beside it.
        var stored = await h.Db.AssistantMessages.IgnoreQueryFilters().ToListAsync();
        stored.Should().ContainSingle();
        stored[0].Role.Should().Be(AssistantMessageRole.User);
        stored[0].Content.Should().Be("What happened with TERM-CC-10?");
    }

    [Fact]
    public async Task A_retry_RE_ANSWERS_the_stored_question_instead_of_asking_it_again()
    {
        // ★ THE DUPLICATION TEST. The backend stores the question BEFORE calling the model, so a retry
        // that re-sent the text would leave the same words in the thread twice — the user watching their
        // own message duplicate as the reward for pressing the button. A retry re-answers what is stored.
        var failing = new FakeProvider(
            failure: new ChatCompletionException(ChatCompletionException.RateLimited, "429"));
        var h = Build(nameof(A_retry_RE_ANSWERS_the_stored_question_instead_of_asking_it_again), failing);
        var conversation = SeedConversation(h);

        await DrainAsync(h.Handler, conversation.Id, "What happened with TERM-CC-10?");

        // The provider recovers; the user presses Retry. The content is deliberately EMPTY to prove the
        // question comes from the stored turn and not from what the client sent.
        var working = new FakeProvider(["It was paid in May."]);
        // Same tenant and same user: a retry is the SAME person asking again, and the ownership gate
        // that guards every other assistant call guards this one unchanged.
        var retryHarness = Build(
            nameof(A_retry_RE_ANSWERS_the_stored_question_instead_of_asking_it_again),
            working, tenantId: h.TenantId);

        var frames = new List<AssistantStreamEvent>();
        await foreach (var frame in retryHarness.Handler.Handle(
            new StreamAssistantReplyCommand(conversation.Id, string.Empty, IsRetry: true),
            CancellationToken.None))
        {
            frames.Add(frame);
        }

        // ONE user row, still. And the model was asked the ORIGINAL question.
        var stored = await retryHarness.Db.AssistantMessages
            .IgnoreQueryFilters().OrderBy(m => m.Sequence).ToListAsync();
        stored.Where(m => m.Role == AssistantMessageRole.User).Should().ContainSingle();
        stored.Should().HaveCount(2, "the answer joined the question that was already there");
        stored[1].Role.Should().Be(AssistantMessageRole.Assistant);
        stored[1].Content.Should().Be("It was paid in May.");

        working.Received![^1].Content.Should().Be("What happened with TERM-CC-10?");

        // No `user` frame: the client already has that row on screen and re-appending would double it.
        frames.Should().NotContain(f => f.Type == "user");
        frames.Should().Contain(f => f.Type == "done");
    }

    [Fact]
    public async Task A_retry_on_a_thread_with_nothing_to_re_answer_refuses_quietly()
    {
        var provider = new FakeProvider(["unused"]);
        var h = Build(nameof(A_retry_on_a_thread_with_nothing_to_re_answer_refuses_quietly), provider);
        var conversation = SeedConversation(h);

        var frames = new List<AssistantStreamEvent>();
        await foreach (var frame in h.Handler.Handle(
            new StreamAssistantReplyCommand(conversation.Id, string.Empty, IsRetry: true),
            CancellationToken.None))
        {
            frames.Add(frame);
        }

        frames.Should().ContainSingle().Which.Type.Should().Be("error");
        (await h.Db.AssistantMessages.IgnoreQueryFilters().ToListAsync()).Should().BeEmpty();
    }

    // ── 1d. ★ The failure SURVIVES a refresh ──────────────────────────────────

    [Fact]
    public async Task A_failed_turn_is_still_reported_as_unanswered_when_the_conversation_is_RELOADED()
    {
        // ★ THE TEST THIS WI STANDS ON. The user asks, the provider fails, the user refreshes. Nothing
        // of the browser's state survives that — so the failure has to be readable from what was
        // STORED. It is: the question is committed before the model is called and the reply only after
        // it succeeds, so a thread ending on a user turn IS the record of the failure.
        //
        // Break the derivation (have UnansweredTurn always return false) and this goes red — which is
        // the moment the user starts losing their question again.
        var provider = new FakeProvider(
            failure: new ChatCompletionException(ChatCompletionException.RateLimited, "429"));
        var h = Build(nameof(A_failed_turn_is_still_reported_as_unanswered_when_the_conversation_is_RELOADED),
            provider);
        var conversation = SeedConversation(h);

        await DrainAsync(h.Handler, conversation.Id, "what happened with TERM-CC-10?");

        // The refresh: everything is re-derived from the rows, with no help from the failed request.
        var stored = await h.Db.AssistantMessages
            .IgnoreQueryFilters().Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.Sequence).ToListAsync();

        var reloaded = AssistantMapper.ToDto(conversation, stored);

        reloaded.LastTurnUnanswered.Should().BeTrue("the reply never arrived, and the thread shows it");
        reloaded.Messages.Should().ContainSingle();
        reloaded.Messages[0].Content.Should().Be("what happened with TERM-CC-10?",
            "the question is still there to be re-answered");
    }

    [Fact]
    public async Task A_SUCCESSFUL_retry_clears_the_failure_for_good()
    {
        // The other half: once it is answered, a refresh must show the answer and NOT the warning.
        // Nothing has to remember to clear a flag — the flag does not exist; the answer's presence is
        // what makes the thread answered.
        var failing = new FakeProvider(
            failure: new ChatCompletionException(ChatCompletionException.RateLimited, "429"));
        var h = Build(nameof(A_SUCCESSFUL_retry_clears_the_failure_for_good), failing);
        var conversation = SeedConversation(h);

        await DrainAsync(h.Handler, conversation.Id, "what happened with TERM-CC-10?");

        var working = new FakeProvider(["It was paid in May."]);
        var retryHarness = Build(
            nameof(A_SUCCESSFUL_retry_clears_the_failure_for_good), working, tenantId: h.TenantId);

        await foreach (var _ in retryHarness.Handler.Handle(
            new StreamAssistantReplyCommand(conversation.Id, string.Empty, IsRetry: true),
            CancellationToken.None))
        {
            // drain
        }

        var stored = await retryHarness.Db.AssistantMessages
            .IgnoreQueryFilters().Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.Sequence).ToListAsync();

        AssistantMapper.ToDto(conversation, stored).LastTurnUnanswered
            .Should().BeFalse("the answer is there now, so nothing is waiting");

        // And still ONE question — the retry re-answered rather than re-asked.
        stored.Where(m => m.Role == AssistantMessageRole.User).Should().ContainSingle();
    }

    [Fact]
    public async Task An_ANSWERED_turn_is_never_reported_as_failed()
    {
        var provider = new FakeProvider(["A real answer."]);
        var h = Build(nameof(An_ANSWERED_turn_is_never_reported_as_failed), provider);
        var conversation = SeedConversation(h);

        await DrainAsync(h.Handler, conversation.Id, "hello");

        var stored = await h.Db.AssistantMessages
            .IgnoreQueryFilters().OrderBy(m => m.Sequence).ToListAsync();

        AssistantMapper.ToDto(conversation, stored).LastTurnUnanswered.Should().BeFalse();
    }

    [Fact]
    public void An_EMPTY_conversation_has_nothing_to_retry()
    {
        // A thread nobody has spoken in is not a failed one. Getting this wrong would greet every new
        // conversation with a warning that something went wrong.
        var conversation = AssistantConversation.Start(Guid.NewGuid(), Guid.NewGuid(), AliceId, "Chat", Now);

        AssistantMapper.ToDto(conversation, []).LastTurnUnanswered.Should().BeFalse();
    }

    [Fact]
    public void The_unanswered_check_reads_SEQUENCE_and_not_the_order_it_was_handed()
    {
        // ★ Ordering by timestamp would be a coin flip: on the non-streaming path a question and its
        // answer are written in the SAME instant. So the check sorts by sequence itself rather than
        // trusting the caller — and this proves it, by handing them over backwards.
        var conversationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var question = AssistantMessage.Create(
            Guid.NewGuid(), conversationId, tenantId, AssistantMessageRole.User, "q", 0, Now);
        var answer = AssistantMessage.Create(
            Guid.NewGuid(), conversationId, tenantId, AssistantMessageRole.Assistant, "a", 1, Now);

        UnansweredTurn.Exists([answer, question]).Should().BeFalse("sequence 1 is the last turn");
        UnansweredTurn.Exists([question]).Should().BeTrue();
    }

    // ── 2. The placeholder is now ONLY the unconfigured fallback ──────────────

    [Fact]
    public async Task The_placeholder_is_gone_when_a_model_answers_and_remains_when_none_is_configured()
    {
        // ★ The rule, stated in one test because it is one rule with two sides.
        var connected = new FakeProvider(["A real answer."]);
        var h1 = Build(nameof(The_placeholder_is_gone_when_a_model_answers_and_remains_when_none_is_configured) + ".on", connected);
        var c1 = SeedConversation(h1);

        await DrainAsync(h1.Handler, c1.Id, "hello");

        var answered = await h1.Db.AssistantMessages.IgnoreQueryFilters()
            .SingleAsync(m => m.Role == AssistantMessageRole.Assistant);
        answered.Content.Should().Be("A real answer.");
        answered.Content.Should().NotBe(AssistantMessage.NotConnectedPlaceholder);

        // No key configured → the stand-in, exactly as before a model existed. A developer without a
        // key gets a working panel rather than an error they cannot fix.
        var unconfigured = new FakeProvider(configured: false);
        var h2 = Build(nameof(The_placeholder_is_gone_when_a_model_answers_and_remains_when_none_is_configured) + ".off", unconfigured);
        var c2 = SeedConversation(h2);

        var frames = await DrainAsync(h2.Handler, c2.Id, "hello");

        frames.Last().Type.Should().Be(AssistantStreamEvent.Done);
        var fallback = await h2.Db.AssistantMessages.IgnoreQueryFilters()
            .SingleAsync(m => m.Role == AssistantMessageRole.Assistant);
        fallback.Content.Should().Be(AssistantMessage.NotConnectedPlaceholder);
        unconfigured.Received.Should().BeNull("an unconfigured provider must not be called at all");
    }

    [Fact]
    public async Task Stand_in_replies_from_the_unconnected_days_are_not_replayed_to_the_model()
    {
        // Feeding "the assistant is not connected yet" back as if the assistant had said it would
        // teach the model to say it again.
        var provider = new FakeProvider(["ok"]);
        var h = Build(nameof(Stand_in_replies_from_the_unconnected_days_are_not_replayed_to_the_model), provider);
        var conversation = SeedConversation(h);

        h.Db.AssistantMessages.AddRange(
            AssistantMessage.Create(Guid.NewGuid(), conversation.Id, h.TenantId, AssistantMessageRole.User, "old question", 0, Now),
            AssistantMessage.Create(Guid.NewGuid(), conversation.Id, h.TenantId, AssistantMessageRole.Assistant,
                AssistantMessage.NotConnectedPlaceholder, 1, Now));
        await h.Db.SaveChangesAsync();

        await DrainAsync(h.Handler, conversation.Id, "new question");

        provider.Received!.Select(m => m.Content).Should().NotContain(AssistantMessage.NotConnectedPlaceholder);
    }

    // ── 3. ★ The provider fails ───────────────────────────────────────────────

    [Fact]
    public async Task A_provider_failure_reaches_the_user_as_a_translation_key_and_stores_nothing()
    {
        var provider = new FakeProvider(
            failure: new ChatCompletionException(ChatCompletionException.RateLimited, "429 from the vendor"));
        var h = Build(nameof(A_provider_failure_reaches_the_user_as_a_translation_key_and_stores_nothing), provider);
        var conversation = SeedConversation(h);

        var frames = await DrainAsync(h.Handler, conversation.Id, "a question");

        var error = frames.Single(f => f.Type == AssistantStreamEvent.Error);
        error.ErrorKey.Should().Be("ASSISTANT.ERROR_RATE_LIMITED");
        // ★ The vendor's own words never reach the client — they can carry request ids and prompt
        // fragments, and the reader needs a sentence in their language, not a status line.
        error.ErrorKey.Should().NotContain("429");
        frames.Should().NotContain(f => f.Type == AssistantStreamEvent.Done);

        // ★ The question survives; NO assistant row was written.
        var stored = await h.Db.AssistantMessages.IgnoreQueryFilters().ToListAsync();
        stored.Should().ContainSingle();
        stored[0].Role.Should().Be(AssistantMessageRole.User);
        stored[0].Content.Should().Be("a question");
    }

    [Fact]
    public async Task A_failure_PART_WAY_THROUGH_stores_nothing_either()
    {
        // The nastier case: fragments were already on screen when the stream died. Storing them would
        // leave a reply that stops mid-sentence and reads as the assistant's considered opinion.
        var provider = new FakeProvider(
            ["The answer begins", " and then"],
            new ChatCompletionException(ChatCompletionException.Unavailable, "connection reset"),
            failAfter: 2);
        var h = Build(nameof(A_failure_PART_WAY_THROUGH_stores_nothing_either), provider);
        var conversation = SeedConversation(h);

        var frames = await DrainAsync(h.Handler, conversation.Id, "a question");

        frames.Count(f => f.Type == AssistantStreamEvent.Fragment).Should().Be(2);
        frames.Last().Type.Should().Be(AssistantStreamEvent.Error);

        (await h.Db.AssistantMessages.IgnoreQueryFilters().CountAsync(m => m.Role == AssistantMessageRole.Assistant))
            .Should().Be(0, "a half-written answer is worse than none");
    }

    [Fact]
    public async Task An_empty_completion_is_treated_as_a_failure_not_as_an_answer()
    {
        // A stream that ends without a word would otherwise persist a blank bubble.
        var provider = new FakeProvider(["   "]);
        var h = Build(nameof(An_empty_completion_is_treated_as_a_failure_not_as_an_answer), provider);
        var conversation = SeedConversation(h);

        var frames = await DrainAsync(h.Handler, conversation.Id, "a question");

        frames.Last().Type.Should().Be(AssistantStreamEvent.Error);
        (await h.Db.AssistantMessages.IgnoreQueryFilters().CountAsync(m => m.Role == AssistantMessageRole.Assistant))
            .Should().Be(0);
    }

    // ── 4. ★ Isolation survived the model ─────────────────────────────────────

    [Fact]
    public async Task A_user_cannot_make_the_model_read_or_write_another_users_conversation()
    {
        // Connecting a model must not have widened anything: the same owned-conversation gate as
        // piece 1. Bob is a colleague in the SAME tenant — the case a tenant filter alone misses.
        const string db = nameof(A_user_cannot_make_the_model_read_or_write_another_users_conversation);
        var tenantId = Guid.NewGuid();

        var aliceProvider = new FakeProvider(["ok"]);
        var alice = Build(db, aliceProvider, AliceId, tenantId);
        var conversation = SeedConversation(alice, AliceId);

        var bobProvider = new FakeProvider(["should never run"]);
        var bob = Build(db, bobProvider, BobId, tenantId);

        var frames = await DrainAsync(bob.Handler, conversation.Id, "read me Alice's chat");

        frames.Should().ContainSingle();
        frames[0].Type.Should().Be(AssistantStreamEvent.Error);
        frames[0].ErrorKey.Should().Be(OwnedConversations.NotFoundKey);

        // ★ The model was never called with someone else's history, and nothing was written.
        bobProvider.Received.Should().BeNull("the model must not see a conversation the caller does not own");
        (await alice.Db.AssistantMessages.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    // ── 5. ★ The key is not reachable from anything the client receives ───────

    [Fact]
    public void No_stream_frame_or_assistant_DTO_has_anywhere_to_put_an_API_key()
    {
        // ★ Structural, not a spot check: the key cannot leak through a shape that has no field for
        // it. Every type the client receives is inspected for a property whose name suggests a secret.
        var clientFacingTypes = new[]
        {
            typeof(AssistantStreamEvent),
            typeof(Wasnie.Application.Assistant.DTOs.AssistantMessageDto),
            typeof(Wasnie.Application.Assistant.DTOs.AssistantConversationDto),
            typeof(Wasnie.Application.Assistant.DTOs.AssistantConversationSummaryDto),
            typeof(Wasnie.Application.Assistant.DTOs.AssistantExchangeDto),
            typeof(Wasnie.Application.Assistant.DTOs.AssistantEntitlementDto),
        };

        var suspicious = new[] { "key", "secret", "token", "credential", "authorization", "apikey" };

        // The one name that legitimately contains "key" and is not one: a TRANSLATION key, which is the
        // whole mechanism by which the provider's own error text is kept away from the browser. Listed
        // explicitly rather than loosening the pattern — "key" must keep tripping this test everywhere
        // else, and the next exception should have to be argued for here too.
        var justified = new[] { $"{nameof(AssistantStreamEvent)}.{nameof(AssistantStreamEvent.ErrorKey)}" };

        foreach (var type in clientFacingTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var qualified = $"{type.Name}.{property.Name}";
                if (justified.Contains(qualified))
                {
                    continue;
                }

                var name = property.Name.ToLowerInvariant();
                suspicious.Should().NotContain(
                    s => name.Contains(s),
                    $"{qualified} must not be able to carry a secret to the browser");
            }
        }

        // And the options type that DOES hold the key is not one of them.
        typeof(GroqOptions).GetProperty(nameof(GroqOptions.ApiKey)).Should().NotBeNull();
        clientFacingTypes.Should().NotContain(typeof(GroqOptions));
    }

    [Fact]
    public async Task The_streamed_frames_carry_only_content_the_user_wrote_or_the_model_returned()
    {
        // The other half of the same guarantee, at runtime: nothing resembling a key rides along.
        var provider = new FakeProvider(["a harmless answer"]);
        var h = Build(nameof(The_streamed_frames_carry_only_content_the_user_wrote_or_the_model_returned), provider);
        var conversation = SeedConversation(h);

        var frames = await DrainAsync(h.Handler, conversation.Id, "hello");

        var serialized = System.Text.Json.JsonSerializer.Serialize(frames);
        serialized.Should().NotContain("test-key", "the configured API key must never reach the client");
        serialized.ToLowerInvariant().Should().NotContain("bearer");
    }
}
