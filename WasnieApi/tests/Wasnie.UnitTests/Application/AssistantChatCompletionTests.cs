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

    private static Harness Build(string dbName, FakeProvider provider, string userId = AliceId, Guid? tenantId = null)
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
